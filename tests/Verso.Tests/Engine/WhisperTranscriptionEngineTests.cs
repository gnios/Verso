using NAudio.Wave;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Whisper.net;

namespace Verso.Tests.Engine;

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
    public async Task TranscribeAsync_CalledTwice_ReloadsFactoryPerJob()
    {
        // Mitigação do crash 0x80131506: a fábrica é invalidada ao fim de cada job,
        // forçando recarga na próxima transcrição (lifetime nativo isolado por job).
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

        // Cada job recarrega a fábrica (Invalidate ao fim de cada job). Trade-off:
        // recarregar o modelo a cada transcrição é mais lento, mas isola o lifetime
        // nativo do WhisperFactory por job — evita acumular centenas de ciclos de
        // create/dispose de processor na mesma fábrica, que corrompia a heap.
        Assert.Equal(2, factoryCache.LoadCount);
        Assert.Equal(2, engine.FactoryLoadCount);
    }

    [Fact]
    public async Task TranscribeAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var wavPath = CreateTempWav(seconds: 3, frequencyHz: 440);
        var modelEnsurer = new NoOpModelEnsurer();
        var factoryCache = new CountingWhisperFactoryCache();
        var processorFactory = new StubWhisperProcessorFactory(
        [
            new WhisperSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), "segmento"),
        ]);

        using var engine = CreateEngine(modelEnsurer, factoryCache, processorFactory);
        // Token pré-cancelado: o engine honra o cancelamento no checkpoint entre etapas
        // (ThrowIfCancellationRequested após EnsureModelAsync), antes de qualquer
        // decodificação nativa. O cancelamento mid-decode foi intencionalmente removido
        // — rasgar o enumerável nativo no meio de whisper_full_with_state era o gatilho
        // de corrupção de heap (ver WhisperProcessorAdapter.ProcessAsync).
        using var cts = new CancellationTokenSource();
        cts.Cancel();

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


    [Fact]
    public async Task TranscribeAsync_WithMultiplePartsAndGpuDevice_UsesParallelPath()
    {
        // Áudio de 5s com silêncio → 3 trechos → 3 partes (maxPartes≥4).
        // Device=Cuda → paralelismo=2 → caminho paralelo.
        var wavPath = CreateMultiPartWav();
        var modelEnsurer = new NoOpModelEnsurer();
        var factoryCache = new CountingWhisperFactoryCache();
        var processorFactory = new StubWhisperProcessorFactory(
        [
            new WhisperSegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(0.5), "fala"),
        ]);

        using var engine = CreateEngine(modelEnsurer, factoryCache, processorFactory);

        var request = new TranscriptionJobRequest(
            Guid.NewGuid(),
            wavPath,
            Language: "pt",
            Quality: ModelQuality.Standard,
            Device: ExecutionDevice.Cuda);

        var result = await engine.TranscribeAsync(request, progress: null, CancellationToken.None);

        // 3 partes × 1 segmento cada = 3 segmentos no total
        Assert.Equal(3, result.Segments.Count);
        // Segmentos ordenados por tempo
        for (var i = 1; i < result.Segments.Count; i++)
            Assert.True(result.Segments[i].StartSeconds >= result.Segments[i - 1].EndSeconds);
        // Factory cacheado não foi usado pelo caminho paralelo (CreateOwnProcessor bypassa)
        // mas LoadCount reflete chamadas ao cache (apenas a LoadModelFactory inicial)
        Assert.Equal(1, factoryCache.LoadCount);
        Assert.Equal(1, engine.FactoryLoadCount);
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
            logger: null,
            mediaPlayback: null,
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

    /// <summary>
    /// Cria WAV de 5s com 3 segmentos de fala intercalados com silêncio (1s fala,
    /// 1s silêncio × 3). SilenceSplitter produz 3 trechos → 3+ partes → paralelismo
    /// quando device = GPU.
    /// </summary>
    private string CreateMultiPartWav()
    {
        var path = Path.Combine(Path.GetTempPath(), $"transcriba-multipart-{Guid.NewGuid():N}.wav");
        _tempFiles.Add(path);

        var sampleRate = AudioLoader.SampleRate;
        const double totalSeconds = 5.0;
        var sampleCount = (int)(totalSeconds * sampleRate);
        var format = new WaveFormat(sampleRate, 16, 1);

        using var writer = new WaveFileWriter(path, format);
        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (double)sampleRate;
            // Fala nos intervalos 0-1s, 2-3s, 4-5s; silêncio (0) nos demais
            var sample = (t % 2 < 1)
                ? (short)(MathF.Sin(2 * MathF.PI * 440 * i / sampleRate) * short.MaxValue * 0.5f)
                : (short)0;
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

    // Modela o cache real: GetOrCreate retorna o factory cacheado se o path não mudou
    // nem foi invalidado; caso contrário "carrega" (LoadCount++) e cacheia. Invalidate
    // descarta o cache atual, forçando recarga na próxima GetOrCreate.
    private sealed class CountingWhisperFactoryCache : IWhisperFactoryCache
    {
        private string? _currentPath;

        public int LoadCount { get; private set; }

        public WhisperFactory GetOrCreate(string modelPath)
        {
            if (_currentPath == modelPath)
                return null!;

            LoadCount++;
            _currentPath = modelPath;
            return null!;
        }

        public void Dispose()
        {
        }

        public void Invalidate(string? modelPath = null) => _currentPath = null;
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

        public IWhisperProcessor CreateOwnProcessor(string modelPath, string language, int threads) =>
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
}