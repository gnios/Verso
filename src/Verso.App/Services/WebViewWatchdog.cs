using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Photino.NET;

namespace Verso.App.Services;

/// <summary>
/// Watchdog do renderer do WebView. O React envia <c>app.ping</c> a cada ~20s; se o
/// renderer morrer (crash do WebView2 → página em branco), as mensagens param e o
/// watchdog recarrega a UI (<see cref="PhotinoWindow.Load"/>, marshaled pelo
/// Photino para a thread da janela). Janela minimizada não conta: timers de página
/// oculta são estrangulados pelo Chromium (~1/min ou pior) e o silêncio é esperado.
/// </summary>
public sealed class WebViewWatchdog : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(150);

    private readonly PhotinoWindow _window;
    private readonly NativeBridge _bridge;
    private readonly string _appUrl;
    private readonly ILogger<WebViewWatchdog> _logger;
    private readonly Timer _timer;
    private int _reloading;

    public WebViewWatchdog(
        PhotinoWindow window,
        NativeBridge bridge,
        string appUrl,
        ILogger<WebViewWatchdog> logger)
    {
        _window = window;
        _bridge = bridge;
        _appUrl = appUrl;
        _logger = logger;
        _timer = new Timer(Check, null, Interval, Interval);
    }

    private void Check(object? state)
    {
        // Minimizada/oculta → timers JS estrangulados → o silêncio não é crash.
        if (_window.Minimized)
        {
            return;
        }

        var silence = DateTime.UtcNow - _bridge.LastMessageAt;
        if (silence <= SilenceLimit || Interlocked.Exchange(ref _reloading, 1) == 1)
        {
            return;
        }

        try
        {
            _logger.LogWarning(
                "WebView sem mensagens há {Seconds:0}s — recarregando UI",
                silence.TotalSeconds);
            _window.Load(_appUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reload do WebView falhou");
        }
        finally
        {
            _reloading = 0;
        }
    }

    public void Dispose() => _timer.Dispose();
}
