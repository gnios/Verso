using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Verso.App.Services;
using Verso.App.ViewModels;
using Verso.Core.Data.Entities;

namespace Verso.Tests.ViewModels;

public class SegmentItemViewModelTests
{
    [Fact]
    public async Task IsActive_ReflectsGetActiveSegmentLogic()
    {
        var (provider, directory, transcriptionId) = await EditorViewModelTests.CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Segments.AddRange(
                [
                    new Segment
                    {
                        Id = Guid.NewGuid(),
                        TranscriptionId = transcription.Id,
                        StartSeconds = 0,
                        EndSeconds = 5,
                        Text = "A",
                        SortOrder = 0,
                    },
                    new Segment
                    {
                        Id = Guid.NewGuid(),
                        TranscriptionId = transcription.Id,
                        StartSeconds = 10,
                        EndSeconds = 15,
                        Text = "B",
                        SortOrder = 1,
                    },
                ]);
            });

        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.NavigateTo(
                ScreenKey.Editor,
                new NavigationParameter(TranscriptionId: transcriptionId));

            var editor = Assert.IsType<EditorViewModel>(navigation.CurrentViewModel);
            await Task.Delay(50);

            editor.SetPlaybackPosition(TimeSpan.FromSeconds(12), markStarted: true);

            Assert.False(editor.Segments[0].IsActive);
            Assert.True(editor.Segments[1].IsActive);
        }
        finally
        {
            Verso.Tests.Services.TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task Click_RaisesSeekRequestForSegmentStart()
    {
        var (provider, directory, transcriptionId) = await EditorViewModelTests.CreateEditorProviderAsync(
            TranscriptionStatus.Done);

        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.NavigateTo(
                ScreenKey.Editor,
                new NavigationParameter(TranscriptionId: transcriptionId));

            var editor = Assert.IsType<EditorViewModel>(navigation.CurrentViewModel);
            await Task.Delay(50);

            double? seekTarget = null;
            editor.SegmentSeekRequested += (_, seconds) => seekTarget = seconds;

            editor.Segments[0].ClickCommand.Execute(null);

            Assert.Equal(0, seekTarget);
        }
        finally
        {
            Verso.Tests.Services.TestDbHelper.Cleanup(directory);
        }
    }
}
