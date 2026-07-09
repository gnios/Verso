using System.Threading;

using Microsoft.Extensions.Logging;

using Verso.Core.Data.Entities;

using Verso.Core.Media;


namespace Verso.Core.Engine;


public interface IModelEnsurer
{
    Task EnsureModelAsync(string modelPath, ModelQuality quality, CancellationToken cancellationToken);
}

public sealed class ModelEnsurer(ModelManager modelManager) : IModelEnsurer
{
    public Task EnsureModelAsync(string modelPath, ModelQuality quality, CancellationToken cancellationToken) =>
        modelManager.EnsureModelAsync(modelPath, quality, cancellationToken);
}

public sealed class WhisperTranscriptionEngine : IDisposable
{
    private readonly AudioLoader _audioLoader;
    private readonly IModelEnsurer _modelEnsurer;
    private readonly IWhisperFactoryCache _factoryCache;
    private readonly IWhisperProcessorFactory _processorFactory;
    private readonly ILogger<WhisperTranscriptionEngine>? _logger;
    private readonly IMediaPlaybackService? _mediaPlayback;
    private readonly string _modelsDirectory;

    public WhisperTranscriptionEngine(
        AudioLoader audioLoader,
        ModelManager modelManager,
        IWhisperFactoryCache? factoryCache = null,
        IWhisperProcessorFactory? processorFactory = null,
        ILogger<WhisperTranscriptionEngine>? logger = null,
        IMediaPlaybackService? mediaPlayback = null,
        string? modelsDirectory = null)
        : this(
            audioLoader,
            new ModelEnsurer(modelManager),
            factoryCache ?? new WhisperFactoryCache(),
            processorFactory,
            logger,
            mediaPlayback,
            modelsDirectory)
    {
    }

    internal WhisperTranscriptionEngine(
        AudioLoader audioLoader,
        IModelEnsurer modelEnsurer,
        IWhisperFactoryCache factoryCache,
        IWhisperProcessorFactory? processorFactory,
        ILogger<WhisperTranscriptionEngine>? logger,
        IMediaPlaybackService? mediaPlayback,
        string? modelsDirectory)
    {
        _audioLoader = audioLoader;
        _modelEnsurer = modelEnsurer;
        _factoryCache = factoryCache;
        _processorFactory = processorFactory ?? new WhisperProcessorFactory(factoryCache);
        _logger = logger;
        _mediaPlayback = mediaPlayback;
        _modelsDirectory = modelsDirectory ?? VersoPaths.ModelsDirectory;
    }

    public int FactoryLoadCount => _factoryCache.LoadCount;

    public async Task<TranscriptionResult> TranscribeAsync(
        TranscriptionJobRequest request,
        IProgress<EngineProgress>? progress,
        CancellationToken cancellationToken)
    {
        WhisperRuntimeConfigurator.Configure(request.Device);
        _logger?.LogInformation(
            "Iniciando transcrição {TranscriptionId}: dispositivo={Device}, modelo={Quality}, idioma={Language}",
            request.TranscriptionId,
            request.Device,
            request.Quality,
            request.Language);
        _logger?.LogDebug(
            "Runtime Whisper (preferência): {RuntimeOrder}",
            string.Join(" → ", WhisperRuntimeConfigurator.ResolveRuntimeOrder(request.Device)));

        Directory.CreateDirectory(_modelsDirectory);
        var modelPath = Path.Combine(_modelsDirectory, ModelManager.GetModelFileName(request.Quality));

        progress?.Report(new EngineProgress("loading"));

        if (_mediaPlayback is not null)
        {
            await _mediaPlayback.UnloadAsync();
        }

        await _modelEnsurer.EnsureModelAsync(modelPath, request.Quality, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var audioPath = await CreateAudioProcessingCopyAsync(request.MediaFilePath, cancellationToken);
        float[] samples;
        try
        {
            samples = await Task.Run(() => _audioLoader.LoadSamples16kHz(audioPath), cancellationToken);
        }
        finally
        {
            TryDeleteFile(audioPath);
        }

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new EngineProgress("preparing"));
        var silenceChunks = SilenceSplitter.SplitBySilence(samples);
        var (maxPartes, _, threadsPorJob) = ChunkPlanner.CalculateParallelLimits(request.Device);
        var partes = ChunkPlanner.GroupParts(silenceChunks, maxPartes);

        LoadModelFactory(modelPath);

        // Mitigação do crash 0x80131506 (causa raiz confirmada via dump em
        // .crash-dumps/Verso.App.exe.18284.dmp): o WhisperFactory cacheado como
        // singleton por modelPath era reutilizado para criar/dispor centenas de
        // WhisperProcessor em sequência, ao longo de MÚLTIPLOS jobs. Esse padrão de
        // reuso é frágil no whisper.net 1.9.1 — o lifetime nativo do factory acumula
        // estado inconsistente entre ciclos de create/dispose e termina corrompendo
        // a heap (sandrohanea/whisper.net#341). Invalide o cache ao fim de cada job
        // força uma fábrica fresca na próxima transcrição, isolando o lifetime nativo
        // por job. Dentro de um job a fábrica é reaproveitada entre chunks (necessário
        // para não recarregar o modelo a cada parte), mas o número de ciclos por job
        // é limitado (max 8 partes, ver ChunkPlanner) — muito menos que as centenas
        // acumuladas entre jobs no design anterior.
        try
        {
            progress?.Report(new EngineProgress("transcribing", 0, partes.Count));

            // Paralelismo é sempre 1 (ver comentário em ChunkPlanner.CalculateParallelLimits):
            // o whisper.net/whisper.cpp não garante segurança para decodificação nativa
            // concorrente a partir do mesmo contexto/modelo (sandrohanea/whisper.net#341).
            // As partes são processadas uma de cada vez, em ordem, usando todos os núcleos
            // disponíveis dentro de cada chamada individual.
            //
            // Cancelamento: apenas entre chunks (ThrowIfCancellationRequested no início de
            // cada iteração). O CancellationToken NÃO é repassado para processor.ProcessAsync
            // — rasgar o enumerável nativo no meio de whisper_full_with_state deixa estado
            // nativo inconsistente e corrompe a heap (ver comentário em
            // WhisperProcessorAdapter.ProcessAsync).
            var segmentos = new List<TranscriptionSegmentResult>();
            for (var index = 0; index < partes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

        var parte = partes[index];
        progress?.Report(new EngineProgress("transcribing", index + 1, partes.Count));

        // try/finally explícito por chunk garante DisposeAsync mesmo se ProcessAsync
        // lançar — não dependemos do scope do `await using`, que pode sobrepor o
        // dispose de um chunk com o create do próximo. O dump mostrou 2
        // WhisperProcessor vivos simultaneamente, o que sugere overlap de
        // lifecycle entre iterações adjacentes.
        var processor = _processorFactory.CreateProcessor(
            modelPath,
            request.Language,
            threadsPorJob);
        try
        {
            await foreach (var result in processor.ProcessAsync(parte.Samples, CancellationToken.None))
            {
                // Mapeia os timestamps do whisper (segundos dentro da parte concatenada,
                // onde o silêncio entre trechos foi removido) de volta para o tempo real
                // do áudio original usando os ChunkSpan. Sem isso, segmentos após o
                // primeiro trecho de uma parte ficam adiantados pelo silêncio acumulado
                // removido, dessincronizando progressivamente do áudio (sintoma: começa
                // certo, no fim totalmente fora).
                var realStart = ChunkPlanner.MapToRealTime(result.Start.TotalSeconds, parte.Chunks);
                var realEnd = ChunkPlanner.MapToRealTime(result.End.TotalSeconds, parte.Chunks);
                segmentos.Add(new TranscriptionSegmentResult(
                    realStart,
                    realEnd,
                    result.Text.Trim()));
            }
        }
        finally
        {
            await processor.DisposeAsync();
        }
            }

            var ordered = segmentos
                .OrderBy(s => s.StartSeconds)
                .ToList();

            progress?.Report(new EngineProgress("done", partes.Count, partes.Count));

            _logger?.LogInformation(
                "Transcrição {TranscriptionId} concluída: {SegmentCount} segmentos em {Parts} partes (runtime {Backend})",
                request.TranscriptionId,
                ordered.Count,
                partes.Count,
                WhisperRuntimeInspector.LoadedBackend ?? "n/d");

            return new TranscriptionResult(ordered);
        }
        finally
        {
            // Fábrica fresca no próximo job: descarta o WhisperFactory nativo ao fim
            // desta transcrição. Sem isto, a mesma fábrica acumularia centenas de ciclos
            // de create/dispose de processor entre jobs, gatilho da corrupção de heap.
            _factoryCache.Invalidate(modelPath);
        }
    }

    public void Dispose() => _factoryCache.Dispose();

    private void LoadModelFactory(string modelPath)
    {
        try
        {
            _factoryCache.GetOrCreate(modelPath);

            // Log do runtime EFETIVAMENTE carregado (RuntimeOptions.LoadedLibrary),
            // que pode diferir da preferência configurada (ex.: Auto sem CUDA/Vulkan
            // cai em CPU). Esta é a linha que responde "qual placa/backend está em uso".
            var loaded = WhisperRuntimeInspector.LoadedRuntime;
            _logger?.LogInformation(
                "Runtime Whisper carregado: {Runtime} (backend {Backend}) — modelo {ModelPath}",
                loaded is null ? "desconhecido" : WhisperRuntimeInspector.GetRuntimeLabel(loaded.Value),
                WhisperRuntimeInspector.GetBackend(loaded) ?? "n/d",
                modelPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Falha ao carregar modelo em {ModelPath}", modelPath);
            throw new InvalidOperationException(
                "Não foi possível inicializar o modelo Whisper. " +
                "Verifique se o runtime selecionado (CPU/CUDA/Vulkan) está disponível ou tente CPU.",
                ex);
        }
    }

    private static async Task<string> CreateAudioProcessingCopyAsync(string mediaPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(mediaPath))
        {
            throw new FileNotFoundException($"Arquivo de mídia não encontrado: {mediaPath}", mediaPath);
        }

        var extension = Path.GetExtension(mediaPath);
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"transcriba-audio-{Guid.NewGuid():N}{extension}");

        await using (var source = new FileStream(
                         mediaPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.ReadWrite))
        await using (var destination = File.Create(tempPath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        return tempPath;
    }

    private static void TryDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}