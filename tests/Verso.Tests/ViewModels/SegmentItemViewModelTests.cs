using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Verso.App.Services;
using Verso.App.ViewModels;
using Verso.Core.Data.Entities;
using Verso.Tests.Services;

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

    [Fact]
    public async Task ShowSpeakerChip_IsTrueOnFirstSegment()
    {
        var (provider, directory, transcriptionId) = await EditorViewModelTests.CreateEditorProviderAsync(
            TranscriptionStatus.Done);
        try
        {
            var editor = await LoadEditorAsync(provider, transcriptionId);

            Assert.True(editor.Segments[0].ShowSpeakerChip);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ShowSpeakerChip_HidesWhenSameSpeakerAsPrevious()
    {
        var speakerId = Guid.NewGuid();
        var (provider, directory, transcriptionId) = await CreateTwoSpeakerSegmentsAsync(
            catalogAnaId: speakerId,
            catalogBetoId: null,
            firstSegmentSpeakerId: speakerId,
            secondSegmentSpeakerId: speakerId);

        try
        {
            var editor = await LoadEditorAsync(provider, transcriptionId);

            Assert.True(editor.Segments[0].ShowSpeakerChip);
            Assert.False(editor.Segments[1].ShowSpeakerChip);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ShowSpeakerChip_ShowsWhenSpeakerChanges()
    {
        var anaId = Guid.NewGuid();
        var betoId = Guid.NewGuid();
        var (provider, directory, transcriptionId) = await CreateTwoSpeakerSegmentsAsync(
            catalogAnaId: anaId,
            catalogBetoId: betoId,
            firstSegmentSpeakerId: anaId,
            secondSegmentSpeakerId: betoId);

        try
        {
            var editor = await LoadEditorAsync(provider, transcriptionId);

            Assert.True(editor.Segments[0].ShowSpeakerChip);
            Assert.True(editor.Segments[1].ShowSpeakerChip);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ShowSpeakerChip_RecalculatesAfterAssigningSpeaker()
    {
        var anaId = Guid.NewGuid();
        var (provider, directory, transcriptionId) = await CreateTwoSpeakerSegmentsAsync(
            catalogAnaId: anaId,
            catalogBetoId: null,
            firstSegmentSpeakerId: anaId,
            secondSegmentSpeakerId: null);

        try
        {
            var editor = await LoadEditorAsync(provider, transcriptionId);
            Assert.True(editor.Segments[0].ShowSpeakerChip);
            Assert.True(editor.Segments[1].ShowSpeakerChip);

            editor.SetPlaybackPosition(TimeSpan.FromSeconds(12), markStarted: true);
            await editor.AssignSpeakerToActiveSegmentAsync(anaId);
            await Task.Delay(50);

            Assert.True(editor.Segments[0].ShowSpeakerChip);
            Assert.False(editor.Segments[1].ShowSpeakerChip);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    private static async Task<EditorViewModel> LoadEditorAsync(IServiceProvider provider, Guid transcriptionId)
    {
        var navigation = provider.GetRequiredService<NavigationService>();
        navigation.NavigateTo(
            ScreenKey.Editor,
            new NavigationParameter(TranscriptionId: transcriptionId));

        var editor = Assert.IsType<EditorViewModel>(navigation.CurrentViewModel);
        await Task.Delay(50);
        return editor;
    }

    private static Task<(IServiceProvider Provider, string Directory, Guid TranscriptionId)>
        CreateTwoSpeakerSegmentsAsync(
            Guid catalogAnaId,
            Guid? catalogBetoId,
            Guid? firstSegmentSpeakerId,
            Guid? secondSegmentSpeakerId) =>
        EditorViewModelTests.CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Speakers.Add(new Speaker
                {
                    Id = catalogAnaId,
                    TranscriptionId = transcription.Id,
                    Name = "Ana",
                    ColorHex = "#2eaadc",
                });
                if (catalogBetoId is Guid otherId)
                {
                    transcription.Speakers.Add(new Speaker
                    {
                        Id = otherId,
                        TranscriptionId = transcription.Id,
                        Name = "Beto",
                        ColorHex = "#e74c3c",
                    });
                }

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
                        SpeakerId = firstSegmentSpeakerId,
                    },
                    new Segment
                    {
                        Id = Guid.NewGuid(),
                        TranscriptionId = transcription.Id,
                        StartSeconds = 10,
                        EndSeconds = 15,
                        Text = "B",
                        SortOrder = 1,
                        SpeakerId = secondSegmentSpeakerId,
                    },
                ]);
            });
}
