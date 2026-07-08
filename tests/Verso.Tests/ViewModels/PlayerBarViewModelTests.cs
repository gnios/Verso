using Verso.App.ViewModels;
using Verso.Tests.Media;

namespace Verso.Tests.ViewModels;

public class PlayerBarViewModelTests
{
    [Fact]
    public void TogglePlay_AlternatesPlayPauseState()
    {
        var playback = new FakeMediaPlaybackService();
        var player = new PlayerBarViewModel(playback);

        player.TogglePlayCommand.Execute(null);
        Assert.True(player.IsPlaying);
        Assert.True(playback.IsPlaying);

        player.TogglePlayCommand.Execute(null);
        Assert.False(player.IsPlaying);
        Assert.False(playback.IsPlaying);
    }

    [Fact]
    public void Seek_CalculatesProportionalPosition()
    {
        var playback = new FakeMediaPlaybackService { Duration = TimeSpan.FromSeconds(100) };
        var player = new PlayerBarViewModel(playback);
        TimeSpan? reported = null;
        player.PositionChanged += (_, position) => reported = position;

        player.SeekCommand.Execute(0.5);

        Assert.NotNull(reported);
        Assert.Equal(TimeSpan.FromSeconds(50), reported);
        Assert.Equal(50, player.ProgressPercent, 1);
    }

    [Fact]
    public void CycleSpeed_FollowsPrototypeSequence()
    {
        var playback = new FakeMediaPlaybackService();
        var player = new PlayerBarViewModel(playback);

        Assert.Equal("1×", player.SpeedLabel);

        player.CycleSpeedCommand.Execute(null);
        Assert.Equal("1.25×", player.SpeedLabel);
        Assert.Equal(1.25f, playback.PlaybackRate);

        player.CycleSpeedCommand.Execute(null);
        Assert.Equal("1.5×", player.SpeedLabel);

        player.CycleSpeedCommand.Execute(null);
        Assert.Equal("2×", player.SpeedLabel);

        player.CycleSpeedCommand.Execute(null);
        Assert.Equal("1×", player.SpeedLabel);
    }

    [Fact]
    public void PlaybackEnd_ResetsPlayIcon()
    {
        var playback = new FakeMediaPlaybackService { Duration = TimeSpan.FromSeconds(30) };
        var player = new PlayerBarViewModel(playback);

        player.TogglePlayCommand.Execute(null);
        Assert.True(player.IsPlaying);

        playback.SimulateEnd();
        player.NotifyPlaybackStopped();

        Assert.False(player.IsPlaying);
    }

    [Fact]
    public async Task LoadAsync_LoadsMediaPath()
    {
        var playback = new FakeMediaPlaybackService();
        var player = new PlayerBarViewModel(playback);

        await player.LoadAsync(@"C:\media\audio.mp3");

        Assert.Equal(@"C:\media\audio.mp3", playback.LoadedPath);
    }
}
