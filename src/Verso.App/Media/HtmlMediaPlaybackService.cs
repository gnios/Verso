using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Verso.Core.Media;

namespace Verso.App.Media;

/// <summary>
/// Reprodutor de áudio cross-platform baseado em <c>&lt;audio&gt;</c> HTML5 controlado via
/// JS interop. Substitui <see cref="NAudioPlaybackService"/> (Windows-only) no Linux/macOS,
/// onde o NAudio (WASAPI/Media Foundation) não funciona.
///
/// O arquivo de áudio local é carregado via um scheme customizado <c>verso-media://</c>
/// (registrado em <c>Program</c>), que faz stream dos bytes do arquivo do disco para o
/// webview — o <c>&lt;audio&gt;</c> não consegue abrir <c>file://</c> dentro do webview
/// isolado, daí o scheme.
///
/// Padrão de anexação do IJSRuntime idêntico ao <c>BlazorThemeApplicator</c>: o
/// <c>MainLayout</c> chama <see cref="AttachJsRuntime"/> no primeiro render. Antes disso,
/// operações de playback ficam pendentes/no-op (o editor só carrega mídia após a navegação,
/// sempre depois do primeiro render).
/// </summary>
public sealed class HtmlMediaPlaybackService : IMediaPlaybackService, IDisposable
{
    private const string Scheme = "verso-media";

    private readonly DotNetObjectReference<HtmlMediaPlaybackService> _dotnetRef;
    private IJSRuntime? _js;
    private string? _loadedPath;
    private float _playbackRate = 1f;
    private int _volume = 100;
    private bool _isPlaying;

    public HtmlMediaPlaybackService()
    {
        _dotnetRef = DotNetObjectReference.Create(this);
    }

    public event EventHandler<TimeSpan>? PositionChanged;

    public TimeSpan Duration { get; private set; }

    public bool IsPlaying => _isPlaying;

    public float PlaybackRate
    {
        get => _playbackRate;
        set
        {
            _playbackRate = value <= 0 ? 1f : value;
            _ = SetRateAsync(_playbackRate);
        }
    }

    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            _ = SetVolumeAsync(_volume);
        }
    }

    public void AttachJsRuntime(IJSRuntime js)
    {
        _js = js;
        _ = InitAsync();
    }

    public async Task LoadAsync(string filePath)
    {
        if (_js is null)
        {
            _loadedPath = filePath;
            return;
        }

        _loadedPath = filePath;
        // O scheme recebe o caminho absoluto codificado (EscapeDataString) para evitar
        // problemas com barras/espaços/acentos no path virarem parte da URL.
        var encoded = Uri.EscapeDataString(filePath);
        var url = $"{Scheme}://localhost/{encoded}";
        await _js.InvokeVoidAsync("versoPlayback.load", url, _playbackRate, _volume);
    }

    public async Task UnloadAsync()
    {
        _loadedPath = null;
        _isPlaying = false;
        Duration = TimeSpan.Zero;
        if (_js is not null)
            await _js.InvokeVoidAsync("versoPlayback.unload");
    }

    public void Play()
    {
        _isPlaying = true;
        if (_js is not null)
            _ = _js.InvokeVoidAsync("versoPlayback.play");
    }

    public void Pause()
    {
        _isPlaying = false;
        if (_js is not null)
            _ = _js.InvokeVoidAsync("versoPlayback.pause");
    }

    public void SeekTo(TimeSpan position)
    {
        if (_js is not null)
            _ = _js.InvokeVoidAsync("versoPlayback.seek", position.TotalSeconds);
    }

    // Chamados pelo JS quando o <audio> dispara 'loadedmetadata' (duração conhecida) e
    // 'timeupdate' (posição atual). Invocados via DotNetObjectReference.
    [JSInvokable]
    public void RaiseMetadata(double durationSeconds)
    {
        Duration = TimeSpan.FromSeconds(durationSeconds);
    }

    [JSInvokable]
    public void RaisePositionChanged(double seconds)
    {
        PositionChanged?.Invoke(this, TimeSpan.FromSeconds(seconds));
    }

    [JSInvokable]
    public void RaiseEnded()
    {
        _isPlaying = false;
        PositionChanged?.Invoke(this, Duration);
    }

    private async Task InitAsync()
    {
        try
        {
            await _js!.InvokeVoidAsync("versoPlayback.init", _dotnetRef);
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task SetRateAsync(float rate)
    {
        if (_js is null) return;
        try { await _js.InvokeVoidAsync("versoPlayback.setRate", rate); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task SetVolumeAsync(int vol)
    {
        if (_js is null) return;
        try { await _js.InvokeVoidAsync("versoPlayback.setVolume", vol / 100f); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose() => _dotnetRef.Dispose();
}