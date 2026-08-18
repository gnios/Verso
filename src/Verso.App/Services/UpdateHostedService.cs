using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verso.Core.Update;

namespace Verso.App.Services;

public sealed class UpdateHostedService : IHostedService
{
    private readonly UpdateSession _session;
    private readonly ILogger<UpdateHostedService> _logger;
    private CancellationTokenSource? _run;

    public UpdateHostedService(UpdateSession session, ILogger<UpdateHostedService> logger)
    {
        _session = session;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_session.TryApplyPendingAndRequestExit())
        {
            _logger.LogInformation("Update pendente: updater iniciado, encerrando o app.");
            return Task.CompletedTask;
        }

        _run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = CheckSafeAsync(_run.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _run?.Cancel();
        return Task.CompletedTask;
    }

    private async Task CheckSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _session.CheckInBackgroundAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao verificar atualização.");
        }
    }
}
