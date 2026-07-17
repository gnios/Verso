using LibVLCSharp.Shared;
using VlcMedia = LibVLCSharp.Shared.Media;

namespace Verso.Core.Media;

public sealed class LibVlcPlaybackService : IMediaPlaybackService, IDisposable
{
    private static readonly object InitSync = new();
    private static bool _coreInitialized;

    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly object _sync = new();

    private VlcMedia? _media;
    private bool _disposed;

    public LibVlcPlaybackService()
    {
        EnsureCoreInitialized();
        _libVlc = new LibVLC();
        _mediaPlayer = new MediaPlayer(_libVlc);
        _mediaPlayer.TimeChanged += OnTimeChanged;
    }

    public event EventHandler<TimeSpan>? PositionChanged;

    public TimeSpan Duration
    {
        get
        {
            var length = _mediaPlayer.Length;
            return length > 0 ? TimeSpan.FromMilliseconds(length) : TimeSpan.Zero;
        }
    }

    public bool IsPlaying => _mediaPlayer.IsPlaying;

    public float PlaybackRate
    {
        get => _mediaPlayer.Rate;
        set => _mediaPlayer.SetRate(value <= 0 ? 1f : value);
    }

    public int Volume
    {
        get => _mediaPlayer.Volume;
        set => _mediaPlayer.Volume = Math.Clamp(value, 0, 100);
    }

    public async Task LoadAsync(string filePath)
    {
        ObjectDisposedThrowIf(_disposed);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var media = new VlcMedia(_libVlc, filePath, FromType.FromPath);
        await media.Parse(MediaParseOptions.ParseLocal).ConfigureAwait(false);

        lock (_sync)
        {
            _mediaPlayer.Media = media;
            _media?.Dispose();
            _media = media;
        }
    }

    public Task UnloadAsync()
    {
        ObjectDisposedThrowIf(_disposed);
        lock (_sync)
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Media = null;
            _media?.Dispose();
            _media = null;
        }

        return Task.CompletedTask;
    }

    public void Play()
    {
        ObjectDisposedThrowIf(_disposed);
        _mediaPlayer.Play();
    }

    public void Pause()
    {
        ObjectDisposedThrowIf(_disposed);
        _mediaPlayer.Pause();
    }

    public void SeekTo(TimeSpan position)
    {
        ObjectDisposedThrowIf(_disposed);

        var clamped = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, Duration.Ticks));
        _mediaPlayer.Time = (long)clamped.TotalMilliseconds;
        PositionChanged?.Invoke(this, clamped);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mediaPlayer.TimeChanged -= OnTimeChanged;
        _mediaPlayer.Stop();
        lock (_sync)
        {
            _media?.Dispose();
            _media = null;
        }

        _mediaPlayer.Dispose();
        _libVlc.Dispose();
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e) =>
        PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(e.Time));

    private static void EnsureCoreInitialized()
    {
        if (_coreInitialized)
        {
            return;
        }

        lock (InitSync)
        {
            if (_coreInitialized)
            {
                return;
            }

            LibVLCSharp.Shared.Core.Initialize();
            _coreInitialized = true;
        }
    }

    private static void ObjectDisposedThrowIf(bool disposed)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(LibVlcPlaybackService));
        }
    }
}
