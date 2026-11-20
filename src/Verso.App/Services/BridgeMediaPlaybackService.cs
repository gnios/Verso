using System;
using System.Threading.Tasks;
using Verso.Core.Media;

namespace Verso.App.Services;

/// <summary>
/// <see cref="IMediaPlaybackService"/> para a UI React: o player é um &lt;audio&gt; no React
/// (LocalMediaServer com Range). Os comandos de playback são encaminhados via evento bridge
/// <c>media.command</c>; eventos de posição/duração virão de volta do JS (a implementar junto
/// com a página Editor). Estado espelhado localmente para a API existente continuar funcional.
/// </summary>
public sealed class BridgeMediaPlaybackService : IMediaPlaybackService
{
    private readonly BridgeHost _host;
    private TimeSpan _duration;
    private bool _isPlaying;
    private int _volume = 100;
    private float _playbackRate = 1f;

    public BridgeMediaPlaybackService(BridgeHost host)
    {
        _host = host;
    }

    public event EventHandler<TimeSpan>? PositionChanged;

    public event EventHandler<TimeSpan>? DurationChanged;

    public TimeSpan Duration => _duration;

    public bool IsPlaying => _isPlaying;

    public float PlaybackRate
    {
        get => _playbackRate;
        set
        {
            _playbackRate = value;
            Send("rate", new { rate = value });
        }
    }

    public int Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            Send("volume", new { volume = value });
        }
    }

    public Task LoadAsync(string filePath)
    {
        Send("load", new { path = filePath });
        return Task.CompletedTask;
    }

    public Task UnloadAsync()
    {
        _isPlaying = false;
        Send("unload", null);
        return Task.CompletedTask;
    }

    public void Play()
    {
        _isPlaying = true;
        Send("play", null);
    }

    public void Pause()
    {
        _isPlaying = false;
        Send("pause", null);
    }

    public void SeekTo(TimeSpan position) =>
        Send("seek", new { position = position.TotalSeconds });

    public void SetKnownDuration(TimeSpan duration)
    {
        if (_duration == duration)
        {
            return;
        }

        _duration = duration;
        DurationChanged?.Invoke(this, duration);
    }

    private void Send(string op, object? payload)
    {
        try
        {
            _host.Bridge.SendEvent("media.command", new { op, payload });
        }
        catch (InvalidOperationException)
        {
            // Bridge ainda não anexada (boot).
        }
    }
}