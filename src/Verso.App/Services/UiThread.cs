using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

namespace Verso.App.Services;

internal static class UiThread
{
    private static Dispatcher? _dispatcher;

    public static void AttachDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public static void Invoke(Action action)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            _dispatcher.InvokeAsync(action).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Mesma precaução da mitigação original (ver histórico Avalonia/WPF): nunca
            // executar `action()` como "fallback" fora da UI thread quando o Invoke falha.
            Debug.WriteLine($"UiThread.Invoke falhou; atualização de UI descartada: {ex}");
        }
    }

    public static async Task InvokeAsync(Action action)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await _dispatcher.InvokeAsync(action).ConfigureAwait(true);
    }
}
