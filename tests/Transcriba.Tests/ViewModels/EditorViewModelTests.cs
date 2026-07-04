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
    internal static async Task<(IServiceProvider Provider, string Directory, Guid TranscriptionId)>
        CreateEditorProviderAsync(TranscriptionStatus status, Action<Transcription>? configure = null)
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
                Icon = "🎤",
                Status = status,
                ErrorMessage = status == TranscriptionStatus.Error ? "falha simulada" : null,
                CreatedAt = DateTime.UtcNow,
                DurationSeconds = 120,
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

            configure?.Invoke(transcription);
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

    [Fact]
    public async Task CommitSegmentText_PersistsToDatabase()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.Done);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            var segment = editor.Segments[0];
            segment.Text = "Texto corrigido";
            segment.CommitText();
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var saved = await ctx.Segments.SingleAsync(s => s.Id == segment.Id);
            Assert.Equal("Texto corrigido", saved.Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SplitSegment_UsesFocusedSegmentCaret_NotPlaybackActiveSegment()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Segments.AddRange(
                [
                    new Segment
                    {
                        Id = firstId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 0,
                        EndSeconds = 5,
                        Text = "alpha beta",
                        SortOrder = 0,
                    },
                    new Segment
                    {
                        Id = secondId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 10,
                        EndSeconds = 15,
                        Text = "gamma",
                        SortOrder = 1,
                    },
                ]);
            });

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            editor.SetPlaybackPosition(TimeSpan.FromSeconds(12));
            editor.OnSegmentFocused(editor.Segments[0], caretIndex: 5);
            await editor.SplitSegmentCommand.ExecuteAsync(null);
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segments = await ctx.Segments
                .Where(s => s.TranscriptionId == transcriptionId)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();

            Assert.Equal(3, segments.Count);
            Assert.Equal("alpha", segments[0].Text);
            Assert.Equal("beta", segments[1].Text);
            Assert.Equal("gamma", segments[2].Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task MergeSegment_UsesPlaybackActiveSegment()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Segments.AddRange(
                [
                    new Segment
                    {
                        Id = firstId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 0,
                        EndSeconds = 5,
                        Text = "primeiro",
                        SortOrder = 0,
                    },
                    new Segment
                    {
                        Id = secondId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 10,
                        EndSeconds = 15,
                        Text = "segundo",
                        SortOrder = 1,
                    },
                ]);
            });

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            editor.SetPlaybackPosition(TimeSpan.FromSeconds(12));
            await editor.MergeSegmentCommand.ExecuteAsync(null);
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segments = await ctx.Segments
                .Where(s => s.TranscriptionId == transcriptionId)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();

            Assert.Single(segments);
            Assert.Equal("primeiro segundo", segments[0].Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task CommitTitle_UpdatesBreadcrumbTitle()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.Done);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            editor.Title = "Novo título";
            await editor.CommitTitleCommand.ExecuteAsync(null);
            await Task.Delay(50);

            Assert.Equal("Novo título", editor.Title);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var saved = await ctx.Transcriptions.SingleAsync(t => t.Id == transcriptionId);
            Assert.Equal("Novo título", saved.Title);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
