using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Verso.App;
using Verso.App.Services;
using Verso.App.ViewModels;
using Verso.Core;
using Verso.Core.Data;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Media;
using Verso.Tests.Media;
using Verso.Tests.Services;

namespace Verso.Tests.ViewModels;

public class EditorPlaybackIntegrationTests
{
    private static async Task<(IServiceProvider Provider, string Directory, Guid TranscriptionId, FakeMediaPlaybackService Playback)>
        CreateEditorWithFakePlaybackAsync()
    {
        var (baseProvider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "verso.db");
        var playback = new FakeMediaPlaybackService { Duration = TimeSpan.FromSeconds(30) };

        var services = new ServiceCollection();
        services.AddVersoDatabase(dbPath);
        services.AddVersoEngine();
        services.AddVersoServices();
        services.AddSingleton<IMediaPlaybackService>(playback);
        services.AddVersoAppServices();

        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);

        var transcriptionId = Guid.NewGuid();
        await using (var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync())
        {
            ctx.Transcriptions.Add(new Transcription
            {
                Id = transcriptionId,
                Title = "Integração playback",
                Status = TranscriptionStatus.Done,
                DurationSeconds = 30,
                CreatedAt = DateTime.UtcNow,
                Segments =
                [
                    new Segment
                    {
                        Id = Guid.NewGuid(),
                        TranscriptionId = transcriptionId,
                        StartSeconds = 0,
                        EndSeconds = 10,
                        Text = "Primeiro",
                        SortOrder = 0,
                    },
                    new Segment
                    {
                        Id = Guid.NewGuid(),
                        TranscriptionId = transcriptionId,
                        StartSeconds = 10,
                        EndSeconds = 20,
                        Text = "Segundo",
                        SortOrder = 1,
                    },
                ],
            });
            await ctx.SaveChangesAsync();
        }

        return (provider, directory, transcriptionId, playback);
    }

    [Fact]
    public async Task PlaybackPositionUpdate_ActivatesMatchingSegment()
    {
        var (provider, directory, transcriptionId, playback) = await CreateEditorWithFakePlaybackAsync();
        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.NavigateTo(
                ScreenKey.Editor,
                new NavigationParameter(TranscriptionId: transcriptionId));

            var editor = Assert.IsType<EditorViewModel>(navigation.CurrentViewModel);
            await Task.Delay(50);

            playback.SeekTo(TimeSpan.FromSeconds(5));
            await Task.Delay(20);

            Assert.True(editor.Segments[0].IsActive);
            Assert.False(editor.Segments[1].IsActive);

            playback.SeekTo(TimeSpan.FromSeconds(15));
            await Task.Delay(20);

            Assert.False(editor.Segments[0].IsActive);
            Assert.True(editor.Segments[1].IsActive);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task PlaybackPositionUpdate_RaisesScrollToActiveSegment()
    {
        var (provider, directory, transcriptionId, playback) = await CreateEditorWithFakePlaybackAsync();
        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.NavigateTo(
                ScreenKey.Editor,
                new NavigationParameter(TranscriptionId: transcriptionId));

            var editor = Assert.IsType<EditorViewModel>(navigation.CurrentViewModel);
            await Task.Delay(50);

            SegmentItemViewModel? scrolled = null;
            editor.ScrollToSegmentRequested += (_, segment) => scrolled = segment;

            playback.SeekTo(TimeSpan.FromSeconds(15));
            await Task.Delay(20);

            Assert.NotNull(scrolled);
            Assert.Equal("Segundo", scrolled.Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SegmentClick_SeeksPlayerToSegmentStart()
    {
        var (provider, directory, transcriptionId, playback) = await CreateEditorWithFakePlaybackAsync();
        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.NavigateTo(
                ScreenKey.Editor,
                new NavigationParameter(TranscriptionId: transcriptionId));

            var editor = Assert.IsType<EditorViewModel>(navigation.CurrentViewModel);
            await Task.Delay(50);

            editor.Segments[1].ClickCommand.Execute(null);
            await Task.Delay(20);

            Assert.Equal(TimeSpan.FromSeconds(10), playback.CurrentPosition);
            Assert.True(editor.Segments[1].IsActive);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
