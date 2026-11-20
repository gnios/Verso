using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Verso.App.Services;

/// <summary>
/// Dispatcher da UI do shell Photino (sem Blazor). Captura o <see cref="SynchronizationContext"/>
/// da thread principal no boot (<see cref="Attach"/>, chamado em Program.MainAsync na thread STA);
/// operações disparadas de threads de fila (worker de transcrição, downloads, update) são postadas
/// nele. Sem context anexado (ex.: testes) executa inline, como a versão Blazor fazia com
/// <c>Dispatcher</c> nulo.
/// </summary>
internal static class UiThread
{
    private static SynchronizationContext? _syncContext;
    private static ILogger? _logger;

    public static void Attach() => _syncContext = SynchronizationContext.Current;

    public static void AttachLogger(ILogger logger) => _logger = logger;

    public static bool CheckAccess() => SynchronizationContext.Current == _syncContext;

    public static void Invoke(Action action)
    {
        if (_syncContext is null || CheckAccess())
        {
            action();
            return;
        }

        // Fire-and-forget: GetResult() na thread da fila pode deadlockar com a UI — descarta
        // a atualização em vez de bloquear (mesma política da versão Blazor).
        _ = InvokeCoreAsync(action);
    }

    public static Task InvokeAsync(Action action) => InvokeCoreAsync(action);

    private static async Task InvokeCoreAsync(Action action)
    {
        if (_syncContext is null || CheckAccess())
        {
            action();
            return;
        }

        try
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _syncContext.Post(_ =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);
            await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "UiThread.Invoke falhou; atualização de UI descartada");
        }
    }
}