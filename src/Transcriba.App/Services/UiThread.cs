using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Transcriba.App.Services;

internal static class UiThread
{
    public static void Invoke(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            // Fora de um app WPF real (ex.: testes de unidade), não existe dispatcher
            // para fazer o marshaling — executa direto.
            action();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            dispatcher.Invoke(action);
        }
        catch (Exception ex)
        {
            // Mesma precaução da mitigação original (ver histórico Avalonia): nunca executar
            // `action()` como "fallback" fora da UI thread quando o Invoke falha — isso
            // continua sendo um acesso concorrente inseguro independente do framework.
            Debug.WriteLine($"UiThread.Invoke falhou; atualização de UI descartada: {ex}");
        }
    }

    public static async System.Threading.Tasks.Task InvokeAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action).Task.ConfigureAwait(true);
    }
}
