using Whisper.net;

namespace Transcriba.Core.Engine;

public sealed record WhisperSegmentResult(TimeSpan Start, TimeSpan End, string Text);

public interface IWhisperProcessor : IAsyncDisposable
{
    IAsyncEnumerable<WhisperSegmentResult> ProcessAsync(float[] samples, CancellationToken cancellationToken);
}

public interface IWhisperProcessorFactory
{
    IWhisperProcessor CreateProcessor(string modelPath, string language, int threads);
}

public sealed class WhisperProcessorFactory : IWhisperProcessorFactory
{
    private readonly IWhisperFactoryCache _factoryCache;

    public WhisperProcessorFactory(IWhisperFactoryCache factoryCache)
    {
        _factoryCache = factoryCache;
    }

    public IWhisperProcessor CreateProcessor(string modelPath, string language, int threads)
    {
        var factory = _factoryCache.GetOrCreate(modelPath);
        var processor = factory.CreateBuilder()
            .WithLanguage(language)
            .WithNoContext()
            .WithGreedySamplingStrategy()
            .WithStringPool()
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
            await foreach (var result in processor.ProcessAsync(samples, cancellationToken))
            {
                yield return new WhisperSegmentResult(result.Start, result.End, result.Text);
                processor.Return(result);
            }
        }
    }
}
