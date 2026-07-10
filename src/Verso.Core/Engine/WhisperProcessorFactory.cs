using Whisper.net;

namespace Verso.Core.Engine;

public sealed record WhisperSegmentResult(TimeSpan Start, TimeSpan End, string Text);

public interface IWhisperProcessor : IAsyncDisposable
{
    IAsyncEnumerable<WhisperSegmentResult> ProcessAsync(float[] samples, CancellationToken cancellationToken);
}

public interface IWhisperProcessorFactory
{
    IWhisperProcessor CreateProcessor(string modelPath, string language, int threads);
    /// <summary>
    /// Cria um processador com seu próprio WhisperFactory (não compartilhado).
    /// Necessário para processamento paralelo de partes: cada task precisa de um
    /// contexto nativo independente, pois whisper.net/whisper.cpp não é thread-safe
    /// para decodificação concorrente no mesmo factory (sandrohanea/whisper.net#341).
    /// O processor é dono do factory — ao ser disposto, ambos são liberados.
    /// </summary>
    IWhisperProcessor CreateOwnProcessor(string modelPath, string language, int threads);
}

public sealed class WhisperProcessorFactory(IWhisperFactoryCache factoryCache) : IWhisperProcessorFactory
{
    public IWhisperProcessor CreateProcessor(string modelPath, string language, int threads)
    {
        var factory = factoryCache.GetOrCreate(modelPath);
        var processor = factory.CreateBuilder()
            .WithLanguage(language)
            .WithNoContext()
            .WithGreedySamplingStrategy()
            .WithThreads(threads)
            .Build();

        return new WhisperProcessorAdapter(processor);
    }

    public IWhisperProcessor CreateOwnProcessor(string modelPath, string language, int threads)
    {
        // Cada processor tem seu próprio WhisperFactory (não cacheado, não compartilhado).
        // O adapter OwnWhisperProcessorAdapter é dono do factory: ao dispor o processor,
        // o factory também é disposto. Isso garante que contextos nativos paralelos não
        // compartilhem estado — whisper.net/whisper.cpp corrompe a heap se dois
        // processors do mesmo factory decodificarem concorrentemente.
        var gpuDevice = WhisperRuntimeConfigurator.CurrentGpuDevice;
        var factory = gpuDevice != 0
            ? WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { GpuDevice = gpuDevice })
            : WhisperFactory.FromPath(modelPath);
        var processor = factory.CreateBuilder()
            .WithLanguage(language)
            .WithNoContext()
            .WithGreedySamplingStrategy()
            .WithThreads(threads)
            .Build();
        return new OwnWhisperProcessorAdapter(factory, processor);
    }

    private sealed class WhisperProcessorAdapter(WhisperProcessor processor) : IWhisperProcessor
    {
        public async ValueTask DisposeAsync() => processor.Dispose();

        public async IAsyncEnumerable<WhisperSegmentResult> ProcessAsync(
            float[] samples,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Não repassar o CancellationToken para a chamada nativa: o whisper.cpp não
            // honra cancelamento mid-decode de forma segura em 1.9.1 — rasgar o enumerável
            // nativo no meio de whisper_full_with_state deixa estado nativo inconsistente
            // e corrompe a heap (causa raiz confirmada do crash 0x80131506 via dump em
            // .crash-dumps/Verso.App.exe.18284.dmp: thread de transcrição em P/Invoke
            // em whisper_full_with_state + heap GC em estado inválido +
            // System.ExecutionEngineException). O cancelamento é tratado apenas entre
            // chunks no WhisperTranscriptionEngine (ThrowIfCancellationRequested no
            // início de cada iteração do loop de partes).
            //
            // WithStringPool() foi removido do builder: o pool nativo de strings é o
            // ponto onde vazamentos/corrupção se manifestam quando o ciclo create/dispose
            // de processors se repete muito; sem ele, cada processor gerencia a própria
            // memória de tokens isoladamente.
            await foreach (var result in processor.ProcessAsync(samples, CancellationToken.None))
            {
                yield return new WhisperSegmentResult(result.Start, result.End, result.Text);
                processor.Return(result);
            }
        }
    }

    /// <summary>
    /// Wrapper que é dono tanto do WhisperFactory quanto do WhisperProcessor.
    /// Usado no caminho paralelo (CreateOwnProcessor): ambos são dispostos juntos.
    /// </summary>
    private sealed class OwnWhisperProcessorAdapter(WhisperFactory factory, WhisperProcessor processor) : IWhisperProcessor
    {
        public async ValueTask DisposeAsync()
        {
            processor.Dispose();
            factory.Dispose();
        }

        public async IAsyncEnumerable<WhisperSegmentResult> ProcessAsync(
            float[] samples,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var result in processor.ProcessAsync(samples, CancellationToken.None))
            {
                yield return new WhisperSegmentResult(result.Start, result.End, result.Text);
                processor.Return(result);
            }
        }
    }
}