using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.App;
using Transcriba.App.Services;
using Transcriba.App.ViewModels;
using Transcriba.Core;
using Transcriba.Core.Data;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;
using Transcriba.Tests.Services;

namespace Transcriba.Tests.ViewModels;

public class EditorViewModelTests
{
    private static async Task<(IServiceProvider Provider, string Directory, Guid TranscriptionId)>
        CreateEditorProviderAsync(TranscriptionStatus status)
    {
        var (baseProvider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "transcriba.db");

        var services = new ServiceCollection();
        services.AddTranscribaDatabase(dbPath);
        services.AddTranscribaEngine();
        services.AddTranscribaServices();
        services.AddTranscribaAppServices();
        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);

        var transcriptionId = Guid.NewGuid();
        await using (var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync())
        {
            var transcription = new Transcription
            {
                Id = transcriptionId,
                Title = "Entrevista teste",
                Status = status,
                ErrorMessage = status == TranscriptionStatus.Error ? "falha simulada" : null,
                CreatedAt = DateTime.UtcNow,
            };

            if (status == TranscriptionStatus.Done)
            {
                transcription.Segments.Add(new Segment
                {
                    Id = Guid.NewGuid(),
                    TranscriptionId = transcriptionId,
                    StartSeconds = 0,
                    EndSeconds = 2.5,
                    Text = "Primeiro segmento",
                    SortOrder = 0,
                });
            }

            ctx.Transcriptions.Add(transcription);
            await ctx.SaveChangesAsync();
        }

        return (provider, directory, transcriptionId);
    }

    private static async Task<EditorViewModel> CreateEditorAsync(
        IServiceProvider provider,
        Guid transcriptionId)
    {
        var navigation = provider.GetRequiredService<NavigationService>();
        navigation.NavigateTo(
            ScreenKey.Editor,
            new NavigationParameter(TranscriptionId: transcriptionId));

        var editor = Assert.IsType<EditorViewModel>(navigation.CurrentViewModel);
        await Task.Delay(50);
        return editor;
    }

    [Fact]
    public async Task Initialize_InProgress_ShowsProgressIndicator()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.InProgress);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);

            Assert.True(editor.IsInProgress);
            Assert.Equal("Transcrição em andamento…", editor.StatusMessage);
            Assert.False(editor.HasSegments);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task Initialize_Done_LoadsSegments()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.Done);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);

            Assert.False(editor.IsInProgress);
            Assert.True(editor.HasSegments);
            Assert.Equal("Primeiro segmento", editor.Segments[0].Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task StatusChanged_Done_LoadsSegmentsWithoutManualReload()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.InProgress);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            var queue = provider.GetRequiredService<TranscriptionQueueService>();

            await using (var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync())
            {
                await ctx.Segments
                    .Where(s => s.TranscriptionId == transcriptionId)
                    .ExecuteDeleteAsync();

                await ctx.Transcriptions
                    .Where(t => t.Id == transcriptionId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(t => t.Status, TranscriptionStatus.Done));

                ctx.Segments.Add(new Segment
                {
                    Id = Guid.NewGuid(),
                    TranscriptionId = transcriptionId,
                    StartSeconds = 0,
                    EndSeconds = 1.2,
                    Text = "Segmento gerado",
                    SortOrder = 0,
                });
                await ctx.SaveChangesAsync();
            }

            typeof(TranscriptionQueueService)
                .GetMethod(
                    "RaiseStatusChanged",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(queue, [transcriptionId, TranscriptionStatusChanged.Done, null]);

            await Task.Delay(50);

            Assert.False(editor.IsInProgress);
            Assert.True(editor.HasSegments);
            Assert.Equal("Segmento gerado", editor.Segments[0].Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
