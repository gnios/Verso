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
}