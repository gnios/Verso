namespace Transcriba.Core.Media;

public interface IMediaPlaybackService
{
    Task LoadAsync(string filePath);

    void Play();

    void Pause();

    void SeekTo(TimeSpan position);

    float PlaybackRate { get; set; }

    int Volume { get; set; }

    event EventHandler<TimeSpan>? PositionChanged;

    TimeSpan Duration { get; }

    bool IsPlaying { get; }
}
