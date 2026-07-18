using System.Text.Json;

using Verso.Core.Engine.Worker;

namespace Verso.Core.Engine;

/// <summary>
/// Implementação de <see cref="ITranscriptionEngine"/> que delega a transcrição a um processo
/// <c>Verso.Worker.exe</c> separado, trocando mensagens NDJSON via stdio (ver
/// <c>.specs/features/transcricao-cpu-responsiva/design.md</c>). Isola as chamadas nativas ao
/// Whisper do processo do <c>Verso.App</c> (WPF/WebView2) — crash/heap corruption no worker vira
/// falha de job, sem derrubar o app (R2.4).
/// </summary>
public sealed class WorkerProcessTranscriptionEngine : ITranscriptionEngine
{
    private static readonly TimeSpan DefaultGracefulShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly IWorkerExecutableLocator _locator;
    private readonly IWorkerProcessFactory _processFactory;
    private readonly TimeSpan _gracefulShutdownTimeout;

    public WorkerProcessTranscriptionEngine(
        IWorkerExecutableLocator locator,
        IWorkerProcessFactory processFactory)
        : this(locator, processFactory, DefaultGracefulShutdownTimeout)
    {
    }

    internal WorkerProcessTranscriptionEngine(
        IWorkerExecutableLocator locator,
        IWorkerProcessFactory processFactory,
        TimeSpan gracefulShutdownTimeout)
    {
        _locator = locator;
        _processFactory = processFactory;
        _gracefulShutdownTimeout = gracefulShutdownTimeout;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        TranscriptionJobRequest request,
        IProgress<EngineProgress>? progress,
        CancellationToken cancellationToken)
    {
        var exePath = _locator.Resolve();
        await using var process = _processFactory.Start(exePath);

        await WriteMessageAsync(process, new WorkerJobMessage(request));

        using var cancelRegistration = cancellationToken.Register(() => _ = HandleCancellationAsync(process));

        TranscriptionResult? result = null;
        string? errorMessage = null;

        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line is null)
                break; // EOF: processo encerrou a saída padrão

            if (string.IsNullOrWhiteSpace(line))
                continue;

            WorkerMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<WorkerMessage>(line, WorkerProtocol.JsonOptions);
            }
            catch (JsonException)
            {
                continue; // linha malformada: loga seria ideal, mas sem logger aqui — ignora e continua
            }

            switch (message)
            {
                case WorkerProgressMessage p:
                    progress?.Report(new EngineProgress(p.Stage, p.PartIndex, p.TotalParts));
                    break;
                case WorkerResultMessage r:
                    result = r.Result;
                    break;
                case WorkerErrorMessage e:
                    errorMessage = e.Message;
                    break;
            }

            if (result is not null || errorMessage is not null)
                break;
        }

        // Fecha o lado de escrita do stdin do worker assim que terminamos de ler a saída dele.
        // Sem isso, o loop de escuta de "cancel" do worker (WorkerHost.ListenForCancelLineAsync)
        // fica bloqueado para sempre numa leitura síncrona de Console.In que NÃO respeita
        // CancellationToken (o token só é observado antes de iniciar a leitura, nunca durante) —
        // o processo filho nunca chamaria `return exitCode`, e o WaitForExitAsync abaixo ficaria
        // pendurado indefinidamente (deadlock: pai espera o filho sair, filho espera EOF em stdin
        // que só o pai pode fornecer). Fechar aqui dá ao filho um EOF real, desbloqueando-o.
        try
        {
            process.StandardInput.Close();
        }
        catch
        {
            // Melhor esforço — se o pipe já tiver sido fechado (ex.: cancelamento concorrente
            // já em andamento via HandleCancellationAsync), ignora.
        }

        var exitCode = await process.WaitForExitAsync(CancellationToken.None);

        if (errorMessage is not null)
            throw new InvalidOperationException(errorMessage);

        if (result is not null)
            return result;

        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);

        throw new InvalidOperationException(
            $"O processo worker terminou inesperadamente (exit code {exitCode}) sem devolver resultado.");
    }

    private async Task HandleCancellationAsync(IWorkerProcess process)
    {
        try
        {
            await WriteMessageAsync(process, new WorkerCancelMessage());

            using var timeoutCts = new CancellationTokenSource(_gracefulShutdownTimeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
            }
        }
        catch
        {
            // Melhor esforço: se notificar o cancelamento falhar (ex.: pipe já fechado), garante
            // que o processo não fica pendurado.
            try
            {
                process.Kill();
            }
            catch
            {
            }
        }
    }

    private static async Task WriteMessageAsync(IWorkerProcess process, WorkerMessage message)
    {
        var json = JsonSerializer.Serialize<WorkerMessage>(message, WorkerProtocol.JsonOptions);
        await process.StandardInput.WriteLineAsync(json);
        await process.StandardInput.FlushAsync();
    }
}
