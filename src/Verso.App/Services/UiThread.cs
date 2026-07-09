using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Photino.NET;

namespace Verso.App.Services;

/// <summary>
/// Marshaling para a thread de UI. Cross-platform via <see cref="PhotinoWindow.Invoke"/>,
/// que executa o delegate na thread nativa do message loop do Photino (a mesma que renderiza
/// o Blazor). Substitui o antigo <c>WPF Dispatcher</c> sem mudar a API estática usada pelos
/// ViewModels (<see cref="Invoke"/>/<see cref="InvokeAsync"/>).
///
/// A janela é anexada em <see cref="Initialize"/> a partir de <c>Program</c>, logo após
/// <c>PhotinoBlazorAppBuilder.Build</c>. Antes disso (ex.: em testes de unidade, sem
/// janela real) executa inline, igual ao comportamento do dispatcher WPF nulo.
/// </summary>
internal static class UiThread
{
    private static PhotinoWindow? _window;

    public static void Initialize(PhotinoWindow window) => _window = window;

    public static void Invoke(Action action)
    {
        if (_window is null)
        {
            // Fora de um app desktop real (ex.: testes de unidade): sem janela para fazer o
            // marshaling — executa direto, igual ao dispatcher nulo do WPF.
            action();
            return;
        }

        try
        {
            _window.Invoke(action);
        }
        catch (Exception ex)
        {
            // Mesma precaução do código original: nunca executar `action()` como "fallback"
            // fora da UI thread quando o Invoke falha — continua sendo um acesso concorrente
            // inseguro independente do framework.
            Debug.WriteLine($"UiThread.Invoke falhou; atualização de UI descartada: {ex}");
        }
    }

    public static async Task InvokeAsync(Action action)
    {
        if (_window is null)
        {
            action();
            return;
        }

        await Task.Run(() =>
        {
            try
            {
                _window.Invoke(action);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UiThread.InvokeAsync falhou; atualização de UI descartada: {ex}");
            }
        }).ConfigureAwait(true);
    }
}