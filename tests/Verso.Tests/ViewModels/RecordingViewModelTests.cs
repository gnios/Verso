using Microsoft.Extensions.DependencyInjection;
using Verso.App;
using Verso.App.Services;
using Verso.App.ViewModels;
using Verso.Core;
using Verso.Core.Data;
using Verso.Core.Engine;
using Verso.Tests.Services;

namespace Verso.Tests.ViewModels;

public class RecordingViewModelTests
{
    private static async Task<(IServiceProvider Provider, string Directory)> CreateProviderAsync()
    {
        var (baseProvider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "verso.db");

        var services = new ServiceCollection();
        services.AddVersoDatabase(dbPath);
        services.AddVersoEngine();
        services.AddVersoServices();
        services.AddVersoAppServices();
        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);

        return (provider, directory);
    }

    private static RecordingViewModel CreateRecording(IServiceProvider provider)
    {
        var navigation = provider.GetRequiredService<NavigationService>();
        navigation.NavigateTo(ScreenKey.Recording);
        return Assert.IsType<RecordingViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task InitialState_IsReadyToRecord()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var recording = CreateRecording(provider);

            Assert.Equal("00:00", recording.TimerDisplay);
            Assert.Equal("Pronto para gravar", recording.StatusText);
            Assert.False(recording.IsRecording);
            Assert.False(recording.ShowPauseStop);
            Assert.False(recording.ShowLiveSection);
            Assert.Equal(48, recording.WaveformBars.Count);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task StartRecording_IncrementsTimerOnTicks()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var recording = CreateRecording(provider);

            recording.StartRecordingCommand.Execute(null);
            recording.ProcessTimerTick();
            recording.ProcessTimerTick();
            recording.ProcessTimerTick();

            Assert.Equal("00:03", recording.TimerDisplay);
            Assert.Equal("Gravando…", recording.StatusText);
            Assert.True(recording.IsLive);
            Assert.True(recording.ShowPauseStop);
            Assert.True(recording.ShowLiveSection);
            Assert.NotEmpty(recording.LiveSegments);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task PauseRecording_StopsTimerButKeepsWaveformState()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var recording = CreateRecording(provider);

            recording.StartRecordingCommand.Execute(null);
            recording.ProcessTimerTick();
            recording.ProcessTimerTick();

            recording.TogglePauseCommand.Execute(null);
            recording.ProcessTimerTick();
            recording.ProcessTimerTick();

            Assert.Equal("00:02", recording.TimerDisplay);
            Assert.Equal("Pausado", recording.StatusText);
            Assert.False(recording.IsLive);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ResumeRecording_ContinuesTimer()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var recording = CreateRecording(provider);

            recording.StartRecordingCommand.Execute(null);
            recording.ProcessTimerTick();
            recording.TogglePauseCommand.Execute(null);
            recording.ProcessTimerTick();
            recording.TogglePauseCommand.Execute(null);
            recording.ProcessTimerTick();

            Assert.Equal("00:02", recording.TimerDisplay);
            Assert.Equal("Gravando…", recording.StatusText);
            Assert.True(recording.IsLive);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task Stop_ResetsStateAndNavigatesToEditor()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            var recording = CreateRecording(provider);
            recording.StopNavigationDelay = TimeSpan.Zero;

            recording.StartRecordingCommand.Execute(null);
            recording.ProcessTimerTick();
            recording.ProcessTimerTick();
            await recording.StopRecordingCommand.ExecuteAsync(null);

            Assert.Equal("00:00", recording.TimerDisplay);
            Assert.Equal("Pronto para gravar", recording.StatusText);
            Assert.False(recording.IsRecording);
            Assert.False(recording.ShowPauseStop);
            Assert.False(recording.ShowLiveSection);
            Assert.Equal(ScreenKey.Editor, navigation.CurrentScreen);
            Assert.IsType<EditorViewModel>(navigation.CurrentViewModel);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
