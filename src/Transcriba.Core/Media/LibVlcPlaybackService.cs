using LibVLCSharp.Shared;
using LibMedia = LibVLCSharp.Shared.Media;

namespace Transcriba.Core.Media;

public sealed class LibVlcPlaybackService : IMediaPlaybackService, IDisposable
{
    private static readonly object InitLock = new();
    private static bool _coreInitialized;

    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private readonly Timer _positionTimer;
    private readonly object _sync = new();
    private LibMedia? _currentMedia;
    private bool _disposed;

    public LibVlcPlaybackService()
    {
        EnsureCoreInitialized();
        _libVlc = new LibVLC();
        _player = new MediaPlayer(_libVlc);
        _player.EndReached += OnEndReached;
        _player.Playing += OnPlaying;
        _player.Paused += OnPaused;
        _player.Stopped += OnStopped;

        _positionTimer = new Timer(OnPositionTimerTick, null, Timeout.Infinite, Timeout.Infinite);

        Volume = 100;
        PlaybackRate = 1f;
    }

    public event EventHandler<TimeSpan>? PositionChanged;

    public TimeSpan Duration
    {
        get
        {
            lock (_sync)
            {
                var lengthMs = _player.Length;
                if (lengthMs <= 0 && _currentMedia is not null)
                {
                    lengthMs = _currentMedia.Duration;
                }

                return TimeSpan.FromMilliseconds(Math.Max(0, lengthMs));
            }
        }
    }

    public bool IsPlaying
    {
        get
        {
            lock (_sync)
            {
                return _player.IsPlaying;
            }
        }
    }

    public float PlaybackRate
    {
        get
        {
            lock (_sync)
            {
                return _player.Rate;
            }
        }
        set
        {
            lock (_sync)
            {
                _player.SetRate(value);
            }
        }
    }

    public int Volume
    {
        get
        {
            lock (_sync)
            {
                return _player.Volume;
            }
        }
        set
        {
            lock (_sync)
            {
                _player.Volume = Math.Clamp(value, 0, 100);
            }
        }
    }

    public async Task LoadAsync(string filePath)
    {
        ObjectDisposedThrowIf(_disposed);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        LibMedia media;
        lock (_sync)
        {
            _player.Stop();
            _currentMedia?.Dispose();
            media = new LibMedia(_libVlc, filePath, FromType.FromPath);
        }

        await media.Parse(MediaParseOptions.ParseLocal).ConfigureAwait(false);

        lock (_sync)
        {
            _currentMedia = media;
            _player.Media = _currentMedia;
        }
    }

    public void Play()
    {
        ObjectDisposedThrowIf(_disposed);
        lock (_sync)
        {
            _player.Play();
        }
    }

    public void Pause()
    {
        ObjectDisposedThrowIf(_disposed);
        lock (_sync)
        {
            _player.Pause();
        }
    }

    public void SeekTo(TimeSpan position)
    {
        ObjectDisposedThrowIf(_disposed);
        lock (_sync)
        {
            var maxMs = Math.Max(0, _player.Length);
            var ms = (long)Math.Clamp(position.TotalMilliseconds, 0, maxMs);
            _player.Time = ms;
            PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(_player.Time));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopPositionTimer();
        _positionTimer.Dispose();

        lock (_sync)
        {
            _player.EndReached -= OnEndReached;
            _player.Playing -= OnPlaying;
            _player.Paused -= OnPaused;
            _player.Stopped -= OnStopped;

            _player.Stop();
            _currentMedia?.Dispose();
            _player.Dispose();
            _libVlc.Dispose();
        }
    }

    private static void EnsureCoreInitialized()
    {
        lock (InitLock)
        {
            if (_coreInitialized)
            {
                return;
            }

            LibVLCSharp.Shared.Core.Initialize();
            _coreInitialized = true;
        }
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        StopPositionTimer();
        lock (_sync)
        {
            _player.Stop();
        }
    }

    private void OnPlaying(object? sender, EventArgs e) => StartPositionTimer();

    private void OnPaused(object? sender, EventArgs e) => StopPositionTimer();

    private void OnStopped(object? sender, EventArgs e) => StopPositionTimer();

    private void OnPositionTimerTick(object? state)
    {
        if (_disposed)
        {
            return;
        }

        TimeSpan position;
        lock (_sync)
        {
            if (!_player.IsPlaying)
            {
                return;
            }

            position = TimeSpan.FromMilliseconds(_player.Time);
        }

        PositionChanged?.Invoke(this, position);
    }

    private void StartPositionTimer()
    {
        _positionTimer.Change(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    private void StopPositionTimer()
    {
        _positionTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private static void ObjectDisposedThrowIf(bool disposed)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(LibVlcPlaybackService));
        }
    }
}
