namespace Verso.Core.Media;

public interface IMediaPlaybackService
{
    /// <summary>
    /// Prepara o caminho da mídia. A implementação pode adiar o fetch até o primeiro Play/Seek.
    /// </summary>
    Task LoadAsync(string filePath);

    Task UnloadAsync();

    void Play();

    void Pause();

    void SeekTo(TimeSpan position);

    /// <summary>
    /// Duração conhecida (ex.: coluna do banco) antes do metadata do arquivo chegar.
    /// </summary>
    void SetKnownDuration(TimeSpan duration);

    float PlaybackRate { get; set; }

    int Volume { get; set; }

    event EventHandler<TimeSpan>? PositionChanged;

    event EventHandler<TimeSpan>? DurationChanged;

    TimeSpan Duration { get; }

    bool IsPlaying { get; }
}
