using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Verso.App.Services;

internal static class UiThread
{
    private static Dispatcher? _dispatcher;
    private static ILogger? _logger;

    public static void AttachDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>
    /// Loga falhas de <see cref="InvokeCoreAsync"/> pelo pipeline normal de <see cref="ILogger"/>
    /// (arquivo + console de debug) em vez de <c>Debug.WriteLine</c>, que sem um
    /// <c>Trace.Listener</c> anexado (removido de propósito — ver <c>Program.ConfigureLogging</c>)
    /// não aparece em lugar nenhum.
    /// </summary>
    public static void AttachLogger(ILogger logger) => _logger = logger;

    public static void Invoke(Action action)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            action();
            return;
        }

        // Fire-and-forget: GetAwaiter().GetResult() na thread da fila pode deadlockar
        // com o dispatcher do Blazor/Photino e descartar o Status=Done — UI presa em 100%.
        _ = InvokeCoreAsync(action);
    }

    public static Task InvokeAsync(Action action) => InvokeCoreAsync(action);

    private static async Task InvokeCoreAsync(Action action)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            await _dispatcher.InvokeAsync(action).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Mesma precaução da mitigação original (ver histórico Avalonia/WPF): nunca
            // executar `action()` como "fallback" fora da UI thread quando o Invoke falha.
            _logger?.LogWarning(ex, "UiThread.Invoke falhou; atualização de UI descartada");
        }
    }
}
