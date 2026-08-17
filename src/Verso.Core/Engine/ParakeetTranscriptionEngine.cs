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

        progress?.Report(new EngineProgress("preparing"));
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
        progress?.Report(new EngineProgress("transcribing", 0, 1));

        var threads = TranscriptionThreadsResolver.Resolve(request.MaxTranscriptionThreads);
        if (threads <= 0)
        {
            threads = Math.Max(1, Environment.ProcessorCount / 2);
        }

        var recognizer = GetOrCreateRecognizer(modelDir, threads);
        var segments = await Task.Run(
            () => recognizer.Recognize(samples, cancellationToken),
            cancellationToken);

        progress?.Report(new EngineProgress("done", 1, 1));
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

    private static async Task<string> CreateAudioProcessingCopyAsync(string mediaPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(mediaPath))
        {
            throw new FileNotFoundException($"Arquivo de mídia não encontrado: {mediaPath}", mediaPath);
        }

        var extension = Path.GetExtension(mediaPath);
        var tempPath = Path.Combine(Path.GetTempPath(), $"verso-parakeet-{Guid.NewGuid():N}{extension}");
        await using var source = new FileStream(mediaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await using var destination = File.Create(tempPath);
        await source.CopyToAsync(destination, cancellationToken);
        return tempPath;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
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
