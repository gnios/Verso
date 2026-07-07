using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.App.Services;
using Transcriba.App.ViewModels;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Services;
using Transcriba.Tests.Services;

namespace Transcriba.Tests.ViewModels;

public class SpeakerDropdownViewModelTests
{
    [Fact]
    public async Task SelectSpeaker_AssignsToActiveSegmentAndClosesDropdown()
    {
        var speakerId = Guid.NewGuid();
        var segmentId = Guid.NewGuid();

        var (provider, directory, transcriptionId) = await EditorViewModelTests.CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Speakers.Add(new Speaker
                {
                    Id = speakerId,
                    TranscriptionId = transcription.Id,
                    Name = "Ana",
                    ColorHex = "#2eaadc",
                });
                transcription.Segments.Add(new Segment
                {
                    Id = segmentId,
                    TranscriptionId = transcription.Id,
                    StartSeconds = 0,
                    EndSeconds = 5,
                    Text = "olá",
                    SortOrder = 0,
                });
            });

        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.NavigateTo(
                ScreenKey.Editor,
                new NavigationParameter(TranscriptionId: transcriptionId));

            var editor = Assert.IsType<EditorViewModel>(navigation.CurrentViewModel);
            await Task.Delay(50);

            editor.SetPlaybackPosition(TimeSpan.Zero, markStarted: true);
            editor.SpeakerDropdown.ToggleCommand.Execute(null);
            var option = editor.SpeakerDropdown.Speakers.Single();
            await editor.SpeakerDropdown.SelectSpeakerCommand.ExecuteAsync(option);
            await Task.Delay(50);

            Assert.False(editor.SpeakerDropdown.IsOpen);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segment = await ctx.Segments.SingleAsync(s => s.Id == segmentId);
            Assert.Equal(speakerId, segment.SpeakerId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task AddNewSpeaker_CreatesSpeakerWithPaletteColorAndAssigns()
    {
        var segmentId = Guid.NewGuid();

        var (provider, directory, transcriptionId) = await EditorViewModelTests.CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Segments.Add(new Segment
                {
                    Id = segmentId,
                    TranscriptionId = transcription.Id,
                    StartSeconds = 0,
                    EndSeconds = 5,
                    Text = "olá",
                    SortOrder = 0,
                });
            });

        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.NavigateTo(
                ScreenKey.Editor,
                new NavigationParameter(TranscriptionId: transcriptionId));

            var editor = Assert.IsType<EditorViewModel>(navigation.CurrentViewModel);
            await Task.Delay(50);

            editor.SetPlaybackPosition(TimeSpan.Zero, markStarted: true);
            editor.SpeakerDropdown.NewSpeakerName = "Carlos";
            await editor.SpeakerDropdown.AddNewSpeakerCommand.ExecuteAsync(null);
            await Task.Delay(50);

            using var scope = provider.CreateScope();
            var speakerService = scope.ServiceProvider.GetRequiredService<SpeakerService>();
            var speakers = await speakerService.GetSpeakersAsync(transcriptionId);

            Assert.Single(speakers);
            Assert.Equal("Carlos", speakers[0].Name);
            Assert.Equal("#2eaadc", speakers[0].ColorHex);
            Assert.False(editor.SpeakerDropdown.IsOpen);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segment = await ctx.Segments.SingleAsync(s => s.Id == segmentId);
            Assert.Equal(speakers[0].Id, segment.SpeakerId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task RenameSpeaker_UpdatesPersistedNameAndRefreshesSegmentDisplay()
    {
        var speakerId = Guid.NewGuid();
        var segmentId = Guid.NewGuid();

        var (provider, directory, transcriptionId) = await EditorViewModelTests.CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Speakers.Add(new Speaker
                {
                    Id = speakerId,
                    TranscriptionId = transcription.Id,
                    Name = "Ana",
                    ColorHex = "#2eaadc",
                });
                transcription.Segments.Add(new Segment
                {
                    Id = segmentId,
                    TranscriptionId = transcription.Id,
                    StartSeconds = 0,
                    EndSeconds = 5,
                    Text = "olá",
                    SortOrder = 0,
                    SpeakerId = speakerId,
                });
            });

        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.NavigateTo(
                ScreenKey.Editor,
                new NavigationParameter(TranscriptionId: transcriptionId));
            var editor = Assert.IsType<EditorViewModel>(navigation.CurrentViewModel);
            await Task.Delay(50);

            Assert.Equal("Ana", editor.Segments[0].SpeakerName);

            var option = editor.SpeakerDropdown.Speakers.Single(s => s.Id == speakerId);
            await editor.SpeakerDropdown.RenameSpeakerCommand.ExecuteAsync((option, "Ana Paula"));
            await Task.Delay(50);

            Assert.Equal("Ana Paula", option.Name);
            Assert.Equal("Ana Paula", editor.Segments[0].SpeakerName);

            using var scope = provider.CreateScope();
            var speakerService = scope.ServiceProvider.GetRequiredService<SpeakerService>();
            var speakers = await speakerService.GetSpeakersAsync(transcriptionId);
            Assert.Equal("Ana Paula", speakers.Single().Name);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task WithoutActiveSegment_AssignActionsAreDisabled()
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

            Assert.False(editor.SpeakerDropdown.CanAssign);
            Assert.False(editor.SpeakerDropdown.SelectSpeakerCommand.CanExecute(null));
            Assert.False(editor.SpeakerDropdown.AddNewSpeakerCommand.CanExecute(null));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
