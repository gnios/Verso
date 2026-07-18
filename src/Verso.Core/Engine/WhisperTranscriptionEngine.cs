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
        WhisperRuntimeConfigurator.Configure(request.Device, request.Quality);

        if (WhisperRuntimeConfigurator.VramFallbackReason is { } reason)
        {
            _logger?.LogWarning("Fallback para CPU: {Reason}", reason);
        }
        _logger?.LogInformation(
            "Iniciando transcrição {TranscriptionId}: dispositivo={Device}, modelo={Quality}, idioma={Language}",
            request.TranscriptionId,
            request.Device,
            request.Quality,
            request.Language);
        if (request.Device == ExecutionDevice.Vulkan && OperatingSystem.IsWindows())
        {
            var devices = VulkanDeviceEnumerator.TryEnumerateDevices();
            if (devices.Count == 0)
            {
                _logger?.LogWarning("Vulkan: nenhum dispositivo físico encontrado (vulkan-1.dll ou driver ausente?)");
            }
            else
            {
                foreach (var d in devices)
                    _logger?.LogInformation("Vulkan device[{Index}]: {DeviceType} — {Name}", d.Index, d.DeviceType, d.Name);
            }
        }
        _logger?.LogInformation(
            "Runtime Whisper (preferência): {RuntimeOrder}",
            string.Join(" → ", WhisperRuntimeInspector.DescribeRuntimeOrder(request.Device)));

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
        var threadsOverride = TranscriptionThreadsResolver.Resolve(request.MaxTranscriptionThreads);
        var (maxPartes, paralelismo, threadsPorJob) = ChunkPlanner.CalculateParallelLimits(request.Device, threadsOverride);
        var partes = ChunkPlanner.GroupParts(silenceChunks, maxPartes);

        LoadModelFactory(modelPath);

        // Histórico: o crash 0x80131506 (causa raiz confirmada via dump em
        // .crash-dumps/Verso.App.exe.18284.dmp) era mitigado invalidando o
        // WhisperFactory cacheado ao fim de cada job, forçando recarga na próxima
        // transcrição. Essa mitigação foi retirada (R2.4/transcricao-cpu-responsiva):
        // cada job agora roda em um processo Verso.Worker isolado, então qualquer
        // corrupção de heap nativa fica confinada àquele processo e não derruba o
        // Verso.App nem contamina jobs seguintes — o isolamento por processo
        // substitui o isolamento por invalidação de cache, permitindo reaproveitar a
        // fábrica (e o modelo já carregado) entre jobs dentro do mesmo worker.
        {
            progress?.Report(new EngineProgress("transcribing", 0, partes.Count));

            // Cancelamento: apenas entre partes (ThrowIfCancellationRequested no início
            // de cada iteração). O CancellationToken NÃO é repassado para
            // processor.ProcessAsync — rasgar o enumerável nativo no meio de
            // whisper_full_with_state deixa estado nativo inconsistente e corrompe a
            // heap (ver comentário em WhisperProcessorAdapter.ProcessAsync).
            List<TranscriptionSegmentResult> ordered;

            if (paralelismo <= 1)
            {
                // Caminho sequencial (compatível com código existente): reusa o
                // WhisperFactory cacheado, seguro porque só um processor existe por vez.
                var segmentos = new List<TranscriptionSegmentResult>();
                for (var index = 0; index < partes.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var parte = partes[index];
                    _logger?.LogInformation(
                        "Parte {ParteIndex}/{ParteTotal} da transcrição {TranscriptionId}",
                        index + 1,
                        partes.Count,
                        request.TranscriptionId);

                    // try/finally explícito por chunk garante DisposeAsync mesmo se
                    // ProcessAsync lançar — não dependemos do scope do `await using`,
                    // que pode sobrepor o dispose de um chunk com o create do próximo.
                    var processor = _processorFactory.CreateProcessor(
                        modelPath,
                        request.Language,
                        threadsPorJob);
                    try
                    {
                        await foreach (var result in processor.ProcessAsync(parte.Samples, CancellationToken.None))
                        {
                            // Mapeia os timestamps do whisper (segundos dentro da parte
                            // concatenada, onde o silêncio entre trechos foi removido)
                            // de volta para o tempo real do áudio original usando os
                            // ChunkSpan. Sem isso, segmentos após o primeiro trecho de
                            // uma parte ficam adiantados pelo silêncio acumulado removido.
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

                    progress?.Report(new EngineProgress("transcribing", index + 1, partes.Count));
                }

                ordered = segmentos
                    .OrderBy(s => s.StartSeconds)
                    .ToList();
            }
            else
            {
                // Caminho paralelo: cada parte decodifica em seu próprio
                // WhisperFactory + WhisperProcessor (contexto nativo independente).
                // whisper.net/whisper.cpp não é thread-safe para decodificação
                // concorrente no MESMO factory, mas factories separados são seguros
                // porque cada um carrega o modelo em memória nativa própria.
                // Trade-off: N instâncias do modelo em GPU (~1.5GB cada p/ LargeV3Turbo).
                _logger?.LogInformation(
                    "Processamento paralelo de {PartCount} partes (concorrência={Paralelismo})",
                    partes.Count,
                    paralelismo);

                var results = new List<TranscriptionSegmentResult>[partes.Count];
                var semaphore = new SemaphoreSlim(paralelismo);
                var completedParts = 0;
                var tasks = new Task[partes.Count];

                for (var index = 0; index < partes.Count; index++)
                {
                    var idx = index; // captura de loop
                    tasks[idx] = ProcessPartParallelAsync(
                        partes[idx], idx, partes.Count, modelPath,
                        request.Language, threadsPorJob,
                        progress, semaphore, cancellationToken,
                        results, () => Interlocked.Increment(ref completedParts));
                }

                await Task.WhenAll(tasks);

                ordered = results
                    .SelectMany(r => r)
                    .OrderBy(s => s.StartSeconds)
                    .ToList();
            }

            progress?.Report(new EngineProgress("done", partes.Count, partes.Count));

            _logger?.LogInformation(
                "Transcrição {TranscriptionId} concluída: {SegmentCount} segmentos em {Parts} partes (runtime {Backend})",
                request.TranscriptionId,
                ordered.Count,
                partes.Count,
                WhisperRuntimeInspector.LoadedBackend ?? "n/d");

            return new TranscriptionResult(ordered);
        }
    }

    /// <summary>
    /// Processa uma parte em paralelo, com seu próprio WhisperFactory + processor.
    /// O semaphore limita o número de tasks concorrentes (uma por slot de GPU).
    /// </summary>
    private async Task ProcessPartParallelAsync(
        WhisperPart part,
        int index,
        int totalParts,
        string modelPath,
        string language,
        int threads,
        IProgress<EngineProgress>? progress,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken,
        List<TranscriptionSegmentResult>[] results,
        Func<int> incrementCompleted)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger?.LogInformation(
                "Parte {ParteIndex}/{ParteTotal} da transcrição (paralelo)",
                index + 1,
                totalParts);

            // Cada parte cria seu próprio WhisperFactory + WhisperProcessor.
            // O adapter OwnWhisperProcessorAdapter é dono do factory — disposa ambos.
            await using var processor = _processorFactory.CreateOwnProcessor(
                modelPath, language, threads);

            var segmentos = new List<TranscriptionSegmentResult>();
            await foreach (var result in processor.ProcessAsync(part.Samples, CancellationToken.None))
            {
                var realStart = ChunkPlanner.MapToRealTime(result.Start.TotalSeconds, part.Chunks);
                var realEnd = ChunkPlanner.MapToRealTime(result.End.TotalSeconds, part.Chunks);
                segmentos.Add(new TranscriptionSegmentResult(
                    realStart,
                    realEnd,
                    result.Text.Trim()));
            }

            // Contagem de partes concluídas (não o índice): a última parte a terminar
            // pode ter índice N-1 e, se reportássemos index+1, a UI saltaria a 100% cedo.
            var completed = incrementCompleted();
            progress?.Report(new EngineProgress("transcribing", completed, totalParts));
            results[index] = segmentos;
        }
        finally
        {
            semaphore.Release();
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