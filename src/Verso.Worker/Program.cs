using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Verso.Core.Engine;
using Verso.Core.Engine.Worker;

namespace Verso.Worker;

/// <summary>
/// Host de processo do worker de transcrição (R2.1/transcricao-cpu-responsiva): monta um
/// <see cref="ServiceCollection"/> mínimo apenas com as dependências do motor Whisper
/// (<see cref="EngineServiceCollectionExtensions.AddWhisperEngine"/>, sem a fila/hosted service do
/// <c>Verso.App</c>), e encaminha stdio para <see cref="WorkerHost"/>, que fala o protocolo NDJSON
/// com o processo pai (<c>WorkerProcessTranscriptionEngine</c>).
/// </summary>
public static class Program
{
    public static async Task<int> Main()
    {
        TryLowerProcessPriority();

        var services = new ServiceCollection();
        services.AddWhisperEngine();
        services.AddParakeetEngine();

        await using var provider = services.BuildServiceProvider();

        // Resolve os motores CONCRETOS (nunca ITranscriptionEngine): este processo é o worker que
        // WorkerProcessTranscriptionEngine spawna, então resolver a interface aqui recriaria o
        // ciclo processo-worker-de-processo-worker. DispatchingTranscriptionEngine escolhe Whisper
        // ou Parakeet a partir de TranscriptionJobRequest.Engine.
        var whisper = provider.GetRequiredService<WhisperTranscriptionEngine>();
        var parakeet = provider.GetRequiredService<ParakeetTranscriptionEngine>();
        var innerEngine = new DispatchingTranscriptionEngine(
            new WhisperTranscriptionEngineAdapter(whisper),
            parakeet);

        var host = new WorkerHost();
        return await host.RunAsync(Console.In, Console.Out, innerEngine, CancellationToken.None);
    }

    /// <summary>
    /// Melhor esforço (R2.5, opcional): reduz a prioridade do processo worker no Windows para não
    /// competir por CPU com a UI do Verso.App. Silenciosamente ignorado fora do Windows ou se a
    /// API não estiver disponível/permitida.
    /// </summary>
    private static void TryLowerProcessPriority()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // Best-effort: falha ao ajustar prioridade não deve impedir o worker de rodar.
        }
    }
}
