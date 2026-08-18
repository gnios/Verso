using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Verso.Core.Data.Entities;

namespace Verso.Core.Engine;

public interface IParakeetModelEnsurer
{
    Task EnsureModelAsync(string directory, ParakeetModel model, CancellationToken cancellationToken);
}

public sealed class ParakeetModelEnsurer(ParakeetModelManager manager) : IParakeetModelEnsurer
{
    public Task EnsureModelAsync(string directory, ParakeetModel model, CancellationToken cancellationToken) =>
        manager.EnsureModelAsync(directory, model, cancellationToken);
}

public sealed class ParakeetTranscriptionEngine : ITranscriptionEngine, IDisposable
{
    private readonly AudioLoader _audioLoader;
    private readonly IParakeetModelEnsurer _modelEnsurer;
    private readonly IParakeetRecognizerFactory _recognizerFactory;
    private readonly ILogger<ParakeetTranscriptionEngine>? _logger;
    private readonly string _modelsDirectory;
    private readonly Dictionary<string, IParakeetRecognizer> _recognizers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _recognizerLock = new();

    public ParakeetTranscriptionEngine(
        AudioLoader audioLoader,
        ParakeetModelManager modelManager,
        IParakeetRecognizerFactory? recognizerFactory = null,
        ILogger<ParakeetTranscriptionEngine>? logger = null,
        string? modelsDirectory = null)
        : this(
            audioLoader,
            new ParakeetModelEnsurer(modelManager),
            recognizerFactory ?? new OnnxParakeetRecognizerFactory(),
            logger,
            modelsDirectory)
    {
    }

    internal ParakeetTranscriptionEngine(
        AudioLoader audioLoader,
        IParakeetModelEnsurer modelEnsurer,
        IParakeetRecognizerFactory recognizerFactory,
        ILogger<ParakeetTranscriptionEngine>? logger,
        string? modelsDirectory)
    {
        _audioLoader = audioLoader;
        _modelEnsurer = modelEnsurer;
        _recognizerFactory = recognizerFactory;
        _logger = logger;
        _modelsDirectory = modelsDirectory ?? VersoPaths.ModelsDirectory;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        TranscriptionJobRequest request,
        IProgress<EngineProgress>? progress,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation(
            "Iniciando transcrição Parakeet {TranscriptionId}: modelo={Model}, idioma={Language} (CPU; Device={Device} ignorado)",
            request.TranscriptionId,
            request.ParakeetModel,
            request.Language,
            request.Device);

        var modelDir = Path.Combine(
            _modelsDirectory,
            ParakeetModelManager.GetModelDirectoryName(request.ParakeetModel));

        progress?.Report(new EngineProgress("loading"));
        await _modelEnsurer.EnsureModelAsync(modelDir, request.ParakeetModel, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(request.MediaFilePath))
        {
            throw new FileNotFoundException(
                $"Arquivo de mídia não encontrado: {request.MediaFilePath}",
                request.MediaFilePath);
        }

        var threads = TranscriptionThreadsResolver.Resolve(request.MaxTranscriptionThreads);
        if (threads <= 0)
        {
            threads = Math.Max(1, Environment.ProcessorCount / 2);
        }

        _logger?.LogInformation(
            "Carregando sessões ONNX Parakeet {TranscriptionId} em {ModelDir} (threads={Threads})",
            request.TranscriptionId,
            modelDir,
            threads);
        var onnxSw = Stopwatch.StartNew();
        var recognizer = GetOrCreateRecognizer(modelDir, threads);
        _logger?.LogInformation(
            "Sessões ONNX prontas em {ElapsedSeconds:F1}s {TranscriptionId}",
            onnxSw.Elapsed.TotalSeconds,
            request.TranscriptionId);

        progress?.Report(new EngineProgress("preparing"));
        var (segments, chunkCount) = await TranscribePcmAsync(
            request,
            recognizer,
            progress,
            cancellationToken);
        progress?.Report(new EngineProgress("done", chunkCount, chunkCount));
        _logger?.LogInformation(
            "Transcrição Parakeet {TranscriptionId} concluída: {SegmentCount} segmentos",
            request.TranscriptionId,
            segments.Count);

        return new TranscriptionResult(segments);
    }

    public void Dispose()
    {
        lock (_recognizerLock)
        {
            foreach (var recognizer in _recognizers.Values)
            {
                (recognizer as IDisposable)?.Dispose();
            }

            _recognizers.Clear();
        }
    }

    private async Task<(IReadOnlyList<TranscriptionSegmentResult> Segments, int ChunkCount)> TranscribePcmAsync(
        TranscriptionJobRequest request,
        IParakeetRecognizer recognizer,
        IProgress<EngineProgress>? progress,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation(
            "Carregando PCM 16 kHz {TranscriptionId}",
            request.TranscriptionId);

        var decodeSw = Stopwatch.StartNew();
        var samples = await Task.Run(
            () => _audioLoader.LoadSamples16kHz(request.MediaFilePath),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var audioSeconds = samples.Length / (double)AudioLoader.SampleRate;
        var chunkCount = Math.Max(1, ParakeetAudioChunker.CountWindows(samples.Length));
        _logger?.LogInformation(
            "PCM 16 kHz pronto em {ElapsedSeconds:F1}s ({SampleCount} samples, {AudioSeconds:F1}s, {WindowCount} janelas de {Window}s overlap {Overlap}s) {TranscriptionId}",
            decodeSw.Elapsed.TotalSeconds,
            samples.Length,
            audioSeconds,
            chunkCount,
            ParakeetAudioChunker.WindowSeconds,
            ParakeetAudioChunker.OverlapSeconds,
            request.TranscriptionId);

        progress?.Report(new EngineProgress("transcribing", 0, chunkCount));
        var segments = await Task.Run(
            () => recognizer.Recognize(samples, cancellationToken, progress),
            cancellationToken);
        return (segments, chunkCount);
    }

    private IParakeetRecognizer GetOrCreateRecognizer(string modelDir, int threads)
    {
        lock (_recognizerLock)
        {
            if (_recognizers.TryGetValue(modelDir, out var existing))
            {
                return existing;
            }

            var created = _recognizerFactory.Create(modelDir, threads);
            _recognizers[modelDir] = created;
            return created;
        }
    }
}

public sealed class DispatchingTranscriptionEngine(
    ITranscriptionEngine whisper,
    ITranscriptionEngine parakeet) : ITranscriptionEngine
{
    public Task<TranscriptionResult> TranscribeAsync(
        TranscriptionJobRequest request,
        IProgress<EngineProgress>? progress,
        CancellationToken cancellationToken) =>
        request.Engine == TranscriptionEngineKind.Parakeet
            ? parakeet.TranscribeAsync(request, progress, cancellationToken)
            : whisper.TranscribeAsync(request, progress, cancellationToken);
}
