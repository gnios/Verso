using NAudio.Wave;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;
using Whisper.net;

namespace Transcriba.Tests.Engine;

public class WhisperTranscriptionEngineTests : IDisposable
{
    [Fact]
    public async Task TranscribeAsync_WithMockedProcessor_ProducesSegmentsWithoutDownloadingModel()
    {
        var wavPath = CreateTempWav(seconds: 2, frequencyHz: 440);
        var modelEnsurer = new NoOpModelEnsurer();
        var factoryCache = new CountingWhisperFactoryCache();
        var processorFactory = new StubWhisperProcessorFactory(
        [
            new WhisperSegmentResult(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1), " olá "),
            new WhisperSegmentResult(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "mundo"),
        ]);

        using var engine = CreateEngine(modelEnsurer, factoryCache, processorFactory);

        var request = CreateRequest(wavPath);
        var result = await engine.TranscribeAsync(request, progress: null, CancellationToken.None);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal("olá", result.Segments[0].Text);
        Assert.Equal(0, result.Segments[0].StartSeconds, precision: 2);
        Assert.Equal("mundo", result.Segments[1].Text);
        Assert.Equal(1, factoryCache.LoadCount);
    }

    [Fact]
    public async Task TranscribeAsync_CalledTwiceWithSameModel_ReusesWhisperFactory()
    {
        var wavPath = CreateTempWav(seconds: 1, frequencyHz: 440);
        var modelEnsurer = new NoOpModelEnsurer();
        var factoryCache = new CountingWhisperFactoryCache();
        var processorFactory = new StubWhisperProcessorFactory(
        [
            new WhisperSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), "teste"),
        ]);

        using var engine = CreateEngine(modelEnsurer, factoryCache, processorFactory);
        var request = CreateRequest(wavPath);

        await engine.TranscribeAsync(request, progress: null, CancellationToken.None);
        await engine.TranscribeAsync(request with { TranscriptionId = Guid.NewGuid() }, progress: null, CancellationToken.None);

        Assert.Equal(1, factoryCache.LoadCount);
        Assert.Equal(1, engine.FactoryLoadCount);
    }

    [Fact]
    public async Task TranscribeAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var wavPath = CreateTempWav(seconds: 3, frequencyHz: 440);
        var modelEnsurer = new NoOpModelEnsurer();
        var factoryCache = new CountingWhisperFactoryCache();
        var processorFactory = new DelayingWhisperProcessorFactory(TimeSpan.FromSeconds(2));

        using var engine = CreateEngine(modelEnsurer, factoryCache, processorFactory);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.TranscribeAsync(CreateRequest(wavPath), progress: null, cts.Token));
    }

    [Fact]
    public void WhisperFactoryCache_GetOrCreateTwiceWithSamePath_LoadsOnlyOnce()
    {
        var loader = new CountingWhisperFactoryLoader();
        using var cache = new WhisperFactoryCache(loader);

        cache.GetOrCreate("model-a.bin");
        cache.GetOrCreate("model-a.bin");

        Assert.Equal(1, loader.LoadCount);
        Assert.Equal(1, cache.LoadCount);
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private readonly List<string> _tempFiles = [];
    private readonly string _modelsDirectory = Path.Combine(Path.GetTempPath(), $"transcriba-models-{Guid.NewGuid():N}");

    private WhisperTranscriptionEngine CreateEngine(
        IModelEnsurer modelEnsurer,
        IWhisperFactoryCache factoryCache,
        IWhisperProcessorFactory processorFactory) =>
        new(
            new AudioLoader(new FfmpegLocator()),
            modelEnsurer,
            factoryCache,
            processorFactory,
            _modelsDirectory);

    private TranscriptionJobRequest CreateRequest(string wavPath) =>
        new(
            Guid.NewGuid(),
            wavPath,
            Language: "pt",
            Quality: ModelQuality.Standard,
            Device: ExecutionDevice.Cpu);

    private string CreateTempWav(double seconds, float frequencyHz)
    {
        var path = Path.Combine(Path.GetTempPath(), $"transcriba-test-{Guid.NewGuid():N}.wav");
        _tempFiles.Add(path);

        var sampleRate = AudioLoader.SampleRate;
        var sampleCount = (int)(seconds * sampleRate);
        var format = new WaveFormat(sampleRate, 16, 1);

        using var writer = new WaveFileWriter(path, format);
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(MathF.Sin(2 * MathF.PI * frequencyHz * i / sampleRate) * short.MaxValue * 0.5f);
            writer.WriteSample(sample / (float)short.MaxValue);
        }

        return path;
    }

    private sealed class NoOpModelEnsurer : IModelEnsurer
    {
        public Task EnsureModelAsync(string modelPath, ModelQuality quality, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
            if (!File.Exists(modelPath))
                File.WriteAllText(modelPath, "mock-model");
            return Task.CompletedTask;
        }
    }

    private sealed class CountingWhisperFactoryCache : IWhisperFactoryCache
    {
        private readonly HashSet<string> _loadedPaths = new(StringComparer.OrdinalIgnoreCase);

        public int LoadCount { get; private set; }

        public WhisperFactory GetOrCreate(string modelPath)
        {
            if (_loadedPaths.Add(modelPath))
                LoadCount++;

            return null!;
        }

        public void Dispose()
        {
        }
    }

    private sealed class CountingWhisperFactoryLoader : IWhisperFactoryLoader
    {
        public int LoadCount { get; private set; }

        public WhisperFactory Load(string modelPath)
        {
            LoadCount++;
            return null!;
        }
    }

    private sealed class StubWhisperProcessorFactory(IReadOnlyList<WhisperSegmentResult> segments) : IWhisperProcessorFactory
    {
        public IWhisperProcessor CreateProcessor(string modelPath, string language, int threads) =>
            new StubWhisperProcessor(segments);
    }

    private sealed class StubWhisperProcessor(IReadOnlyList<WhisperSegmentResult> segments) : IWhisperProcessor
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async IAsyncEnumerable<WhisperSegmentResult> ProcessAsync(
            float[] samples,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var segment in segments)
                yield return segment;
        }
    }

    private sealed class DelayingWhisperProcessorFactory(TimeSpan delay) : IWhisperProcessorFactory
    {
        public IWhisperProcessor CreateProcessor(string modelPath, string language, int threads) =>
            new DelayingWhisperProcessor(delay);
    }

    private sealed class DelayingWhisperProcessor(TimeSpan delay) : IWhisperProcessor
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async IAsyncEnumerable<WhisperSegmentResult> ProcessAsync(
            float[] samples,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            yield return new WhisperSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), "late");
        }
    }
}
