using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Verso.Core.Media;

namespace Verso.App.Services;

/// <summary>
/// Player HTML5 no WebView. A mídia é servida por <see cref="LocalMediaServer"/> (HTTP + Range),
/// não pelo scheme Photino — que materializa o arquivo inteiro em memória.
/// O <c>audio.src</c> só é definido no primeiro Play/Seek (lazy), para não atrasar a abertura do Editor.
/// </summary>
public sealed class Html5AudioPlaybackService : IMediaPlaybackService, IAsyncDisposable
{
    private readonly LocalMediaServer _mediaServer;
    private readonly object _sync = new();
    private IJSRuntime? _jsRuntime;
    private DotNetObjectReference<Html5AudioPlaybackService>? _dotNetRef;
    private string? _filePath;
    private bool _srcAttached;
    private bool _isPlaying;
    private float _playbackRate = 1f;
    private int _volume = 100;
    private TimeSpan _duration;
    private bool _disposed;

    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<TimeSpan>? DurationChanged;

    public Html5AudioPlaybackService(LocalMediaServer mediaServer)
    {
        _mediaServer = mediaServer;
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_sync)
            {
                return _duration;
            }
        }
    }

    public bool IsPlaying
    {
        get
        {
            lock (_sync)
            {
                return _isPlaying;
            }
        }
    }

    public float PlaybackRate
    {
        get
        {
            lock (_sync)
            {
                return _playbackRate;
            }
        }
        set
        {
            var rate = value <= 0 ? 1f : value;
            lock (_sync)
            {
                _playbackRate = rate;
            }

            _ = InvokeVoidAsync("versoAudio.setRate", rate);
        }
    }

    public int Volume
    {
        get
        {
            lock (_sync)
            {
                return _volume;
            }
        }
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            lock (_sync)
            {
                _volume = clamped;
            }

            _ = InvokeVoidAsync("versoAudio.setVolume", clamped / 100.0);
        }
    }

    public void AttachJsRuntime(IJSRuntime jsRuntime)
    {
        ObjectDisposedThrowIf();
        ArgumentNullException.ThrowIfNull(jsRuntime);

        _jsRuntime = jsRuntime;
        _dotNetRef?.Dispose();
        _dotNetRef = DotNetObjectReference.Create(this);
        _ = AttachCoreAsync(jsRuntime, _dotNetRef);
    }

    public void SetKnownDuration(TimeSpan duration)
    {
        ObjectDisposedThrowIf();
        var safe = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        lock (_sync)
        {
            // Não sobrescreve metadata real já recebida do <audio>.
            if (_srcAttached && _duration > TimeSpan.Zero)
            {
                return;
            }

            _duration = safe;
        }

        DurationChanged?.Invoke(this, safe);
    }

    public Task LoadAsync(string filePath)
    {
        ObjectDisposedThrowIf();
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        lock (_sync)
        {
            _filePath = filePath;
            _srcAttached = false;
            _isPlaying = false;
            // Mantém duração conhecida (DB) se já setada; zera só se ainda não houver.
        }

        // Lazy: não chama versoAudio.load aqui — evita baixar o arquivo ao abrir a transcrição.
        return Task.CompletedTask;
    }

    public async Task UnloadAsync()
    {
        ObjectDisposedThrowIf();

        bool hadSrc;
        lock (_sync)
        {
            hadSrc = _srcAttached;
            _filePath = null;
            _srcAttached = false;
            _duration = TimeSpan.Zero;
            _isPlaying = false;
        }

        // Só toca no <audio> se algo foi anexado — evita interop JS ao abrir/trocar tela.
        if (hadSrc)
        {
            await InvokeVoidAsync("versoAudio.unload").ConfigureAwait(false);
        }

        DurationChanged?.Invoke(this, TimeSpan.Zero);
    }

    public void Play()
    {
        ObjectDisposedThrowIf();
        lock (_sync)
        {
            _isPlaying = true;
        }

        _ = PlayCoreAsync();
    }

    public void Pause()
    {
        ObjectDisposedThrowIf();
        lock (_sync)
        {
            _isPlaying = false;
        }

        _ = InvokeVoidAsync("versoAudio.pause");
    }

    public void SeekTo(TimeSpan position)
    {
        ObjectDisposedThrowIf();
        _ = SeekCoreAsync(position);
    }

    [JSInvokable]
    public void OnPositionChanged(double seconds)
    {
        var position = TimeSpan.FromSeconds(Math.Max(0, seconds));
        PositionChanged?.Invoke(this, position);
    }

    [JSInvokable]
    public void OnDurationChanged(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        lock (_sync)
        {
            _duration = duration;
        }

        DurationChanged?.Invoke(this, duration);
    }

    [JSInvokable]
    public void OnEnded()
    {
        lock (_sync)
        {
            _isPlaying = false;
        }

        PositionChanged?.Invoke(this, Duration);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await InvokeVoidAsync("versoAudio.unload").ConfigureAwait(false);
        }
        catch
        {
            // WebView já pode ter sido destruído.
        }

        _dotNetRef?.Dispose();
        _dotNetRef = null;
        _jsRuntime = null;
    }

    private async Task AttachCoreAsync(IJSRuntime jsRuntime, DotNetObjectReference<Html5AudioPlaybackService> dotNetRef)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("versoAudio.bindDotNet", dotNetRef).ConfigureAwait(false);
            await jsRuntime.InvokeVoidAsync("versoAudio.setVolume", Volume / 100.0).ConfigureAwait(false);
            await jsRuntime.InvokeVoidAsync("versoAudio.setRate", PlaybackRate).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task PlayCoreAsync()
    {
        try
        {
            if (!await EnsureSrcAttachedAsync().ConfigureAwait(false))
            {
                lock (_sync)
                {
                    _isPlaying = false;
                }

                return;
            }

            await InvokeVoidAsync("versoAudio.play").ConfigureAwait(false);
        }
        catch
        {
            lock (_sync)
            {
                _isPlaying = false;
            }
        }
    }

    private async Task SeekCoreAsync(TimeSpan position)
    {
        try
        {
            if (!await EnsureSrcAttachedAsync().ConfigureAwait(false))
            {
                return;
            }

            var duration = Duration;
            var clamped = duration > TimeSpan.Zero
                ? TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, duration.Ticks))
                : (position < TimeSpan.Zero ? TimeSpan.Zero : position);

            await InvokeVoidAsync("versoAudio.seek", clamped.TotalSeconds).ConfigureAwait(false);
            PositionChanged?.Invoke(this, clamped);
        }
        catch
        {
            // ignore
        }
    }

    private async Task<bool> EnsureSrcAttachedAsync()
    {
        string? path;
        bool alreadyAttached;
        lock (_sync)
        {
            path = _filePath;
            alreadyAttached = _srcAttached;
        }

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (alreadyAttached)
        {
            return true;
        }

        if (!_mediaServer.IsRunning)
        {
            return false;
        }

        var url = _mediaServer.BuildUrl(path);
        await InvokeVoidAsync("versoAudio.load", url).ConfigureAwait(false);
        await InvokeVoidAsync("versoAudio.setVolume", Volume / 100.0).ConfigureAwait(false);
        await InvokeVoidAsync("versoAudio.setRate", PlaybackRate).ConfigureAwait(false);

        lock (_sync)
        {
            _srcAttached = true;
        }

        return true;
    }

    private async Task InvokeVoidAsync(string identifier, params object?[] args)
    {
        var js = _jsRuntime;
        if (js is null || _disposed)
        {
            return;
        }

        try
        {
            await js.InvokeVoidAsync(identifier, args).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
            // JS interop indisponível fora do circuito Blazor (testes headless).
        }
    }

    private void ObjectDisposedThrowIf()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Html5AudioPlaybackService));
        }
    }
}
