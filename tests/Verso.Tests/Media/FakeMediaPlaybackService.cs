using Verso.Core.Media;

namespace Verso.Tests.Media;

internal sealed class FakeMediaPlaybackService : IMediaPlaybackService
{
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<TimeSpan>? DurationChanged;

    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(2);
    public bool IsPlaying { get; private set; }
    public float PlaybackRate { get; set; } = 1f;
    public int Volume { get; set; } = 100;
    public TimeSpan CurrentPosition { get; private set; }

    public Task LoadAsync(string filePath)
    {
        LoadedPath = filePath;
        return Task.CompletedTask;
    }

    public Task UnloadAsync()
    {
        LoadedPath = null;
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public void SetKnownDuration(TimeSpan duration)
    {
        Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        DurationChanged?.Invoke(this, Duration);
    }

    public string? LoadedPath { get; private set; }

    public void Play() => IsPlaying = true;

    public void Pause() => IsPlaying = false;

    public void SeekTo(TimeSpan position)
    {
        CurrentPosition = position;
        PositionChanged?.Invoke(this, position);
    }

    public void SimulateEnd()
    {
        IsPlaying = false;
        CurrentPosition = Duration;
        PositionChanged?.Invoke(this, Duration);
    }
}
