using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Verso.Core.Media;

namespace Verso.App.Services;

/// <summary>
/// Implementação de <see cref="IMediaPlaybackService"/> sobre HTML5 <c>&lt;audio&gt;</c>
/// no WebView (ver <c>wwwroot/js/audio.js</c>). Segue o padrão de attach de
/// <see cref="BlazorThemeApplicator"/>: <see cref="IJSRuntime"/> só existe no escopo Blazor
/// e é anexado pelo <c>MainLayout</c> no primeiro render.
/// </summary>
public sealed class Html5AudioPlaybackService : IMediaPlaybackService, IAsyncDisposable
{
    private readonly object _sync = new();
    private IJSRuntime? _jsRuntime;
    private DotNetObjectReference<Html5AudioPlaybackService>? _dotNetRef;
    private string? _pendingLoadPath;
    private TaskCompletionSource? _metadataLoaded;
    private bool _isPlaying;
    private float _playbackRate = 1f;
    private int _volume = 100;
    private TimeSpan _duration;
    private bool _disposed;

    public event EventHandler<TimeSpan>? PositionChanged;

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

    public async Task LoadAsync(string filePath)
    {
        ObjectDisposedThrowIf();
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        TaskCompletionSource metadata;
        lock (_sync)
        {
            _duration = TimeSpan.Zero;
            _isPlaying = false;
            _pendingLoadPath = filePath;
            _metadataLoaded?.TrySetCanceled();
            metadata = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _metadataLoaded = metadata;
        }

        if (_jsRuntime is null)
        {
            return;
        }

        var url = MediaSchemeHandler.BuildUrl(filePath);
        await InvokeVoidAsync("versoAudio.load", url).ConfigureAwait(false);
        await InvokeVoidAsync("versoAudio.setVolume", Volume / 100.0).ConfigureAwait(false);
        await InvokeVoidAsync("versoAudio.setRate", PlaybackRate).ConfigureAwait(false);

        // Aguarda metadata (equivalente ao Parse do LibVLC) com timeout generoso.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await metadata.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Timeout ou cancel: Duration pode ficar 0; UI ainda funciona após loadedmetadata tardio.
        }

        lock (_sync)
        {
            if (_pendingLoadPath == filePath)
            {
                _pendingLoadPath = null;
            }
        }
    }

    public async Task UnloadAsync()
    {
        ObjectDisposedThrowIf();

        lock (_sync)
        {
            _pendingLoadPath = null;
            _duration = TimeSpan.Zero;
            _isPlaying = false;
            _metadataLoaded?.TrySetCanceled();
            _metadataLoaded = null;
        }

        await InvokeVoidAsync("versoAudio.unload").ConfigureAwait(false);
    }

    public void Play()
    {
        ObjectDisposedThrowIf();
        lock (_sync)
        {
            _isPlaying = true;
        }

        _ = InvokeVoidAsync("versoAudio.play");
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

        var duration = Duration;
        var clamped = duration > TimeSpan.Zero
            ? TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, duration.Ticks))
            : (position < TimeSpan.Zero ? TimeSpan.Zero : position);

        _ = InvokeVoidAsync("versoAudio.seek", clamped.TotalSeconds);
        PositionChanged?.Invoke(this, clamped);
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
        lock (_sync)
        {
            _duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
            _metadataLoaded?.TrySetResult();
        }
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

            string? pending;
            lock (_sync)
            {
                pending = _pendingLoadPath;
                _pendingLoadPath = null;
            }

            if (!string.IsNullOrEmpty(pending))
            {
                await LoadAsync(pending).ConfigureAwait(false);
            }
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
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
