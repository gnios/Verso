using System.Text.Json;

namespace Verso.Core.Engine.Worker;

/// <summary>
/// Lado worker do protocolo NDJSON: lê a mensagem <c>job</c> inicial de <paramref name="input"/>-like
/// stream, executa o motor de transcrição real, encaminha progresso/resultado/erro via
/// <paramref name="output"/>-like stream, e observa concorrentemente novas linhas de stdin para
/// detectar <c>cancel</c>.
/// </summary>
public sealed class WorkerHost
{
    public async Task<int> RunAsync(
        TextReader input,
        TextWriter output,
        ITranscriptionEngine innerEngine,
        CancellationToken shutdownToken)
    {
        var jobLine = await input.ReadLineAsync(shutdownToken);
        if (string.IsNullOrWhiteSpace(jobLine))
        {
            WriteMessage(output, new WorkerErrorMessage("Nenhuma mensagem 'job' recebida via stdin."));
            return 1;
        }

        WorkerJobMessage job;
        try
        {
            if (JsonSerializer.Deserialize<WorkerMessage>(jobLine, WorkerProtocol.JsonOptions) is not WorkerJobMessage parsedJob)
            {
                WriteMessage(output, new WorkerErrorMessage("Mensagem inicial inválida: esperado 'job'."));
                return 1;
            }

            job = parsedJob;
        }
        catch (JsonException ex)
        {
            WriteMessage(output, new WorkerErrorMessage($"Mensagem 'job' malformada: {ex.Message}"));
            return 1;
        }

        using var stopListeningCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        // Task.Run é obrigatório aqui: Console.In (SyncTextReader) implementa ReadLineAsync como uma
        // leitura SÍNCRONA e bloqueante por baixo dos panos (comportamento documentado do runtime .NET),
        // então chamar ListenForCancelLineAsync diretamente bloquearia a própria thread de RunAsync até
        // chegar uma segunda linha em stdin — travando para sempre antes de TranscribeAsync ser chamado.
        var cancelListenTask = Task.Run(() => ListenForCancelLineAsync(input, linkedCts, stopListeningCts.Token));

        var progress = new SynchronousProgress<EngineProgress>(e =>
            WriteMessage(output, new WorkerProgressMessage(e.Stage, e.PartIndex, e.TotalParts)));

        int exitCode;
        try
        {
            var result = await innerEngine.TranscribeAsync(job.Request, progress, linkedCts.Token);
            WriteMessage(output, new WorkerResultMessage(result));
            exitCode = 0;
        }
        catch (Exception ex)
        {
            WriteMessage(output, new WorkerErrorMessage(ex.Message));
            exitCode = 1;
        }
        finally
        {
            stopListeningCts.Cancel();
        }

        try
        {
            await cancelListenTask;
        }
        catch (OperationCanceledException)
        {
        }

        return exitCode;
    }

    private static async Task ListenForCancelLineAsync(
        TextReader input,
        CancellationTokenSource jobCancellation,
        CancellationToken stopListening)
    {
        while (!stopListening.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await input.ReadLineAsync(stopListening);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (line is null)
                return; // EOF

            if (string.IsNullOrWhiteSpace(line))
                continue;

            WorkerMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<WorkerMessage>(line, WorkerProtocol.JsonOptions);
            }
            catch (JsonException)
            {
                continue; // linha malformada: ignora e continua lendo (mesma política do lado pai)
            }

            if (message is WorkerCancelMessage)
            {
                jobCancellation.Cancel();
                return;
            }
        }
    }

    private static void WriteMessage(TextWriter output, WorkerMessage message)
    {
        var json = JsonSerializer.Serialize<WorkerMessage>(message, WorkerProtocol.JsonOptions);
        output.WriteLine(json);
        output.Flush();
    }

    /// <summary>
    /// <see cref="IProgress{T}"/> que invoca o callback de forma síncrona e imediata na thread
    /// chamadora, ao contrário de <see cref="Progress{T}"/> (que faz post assíncrono via
    /// SynchronizationContext/ThreadPool). Necessário aqui para garantir a ordem das linhas NDJSON
    /// escritas em <paramref name="output"/> — sem isso, mensagens de progresso poderiam ser
    /// escritas fora de ordem em relação ao result/error final.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
