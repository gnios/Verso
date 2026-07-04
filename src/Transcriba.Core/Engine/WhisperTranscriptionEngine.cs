using System.Collections.Concurrent;
using Transcriba.Core.Data.Entities;
using Whisper.net.LibraryLoader;

namespace Transcriba.Core.Engine;

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
    private readonly string _modelsDirectory;

    public WhisperTranscriptionEngine(
        AudioLoader audioLoader,
        ModelManager modelManager,
        IWhisperFactoryCache? factoryCache = null,
        IWhisperProcessorFactory? processorFactory = null,
        string? modelsDirectory = null)
        : this(
            audioLoader,
            new ModelEnsurer(modelManager),
            factoryCache ?? new WhisperFactoryCache(),
            processorFactory,
            modelsDirectory)
    {
    }

    internal WhisperTranscriptionEngine(
        AudioLoader audioLoader,
        IModelEnsurer modelEnsurer,
        IWhisperFactoryCache factoryCache,
        IWhisperProcessorFactory? processorFactory,
        string? modelsDirectory)
    {
        _audioLoader = audioLoader;
        _modelEnsurer = modelEnsurer;
        _factoryCache = factoryCache;
        _processorFactory = processorFactory ?? new WhisperProcessorFactory(factoryCache);
        _modelsDirectory = modelsDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Transcriba", "models");
    }

    public int FactoryLoadCount => _factoryCache.LoadCount;

    public async Task<TranscriptionResult> TranscribeAsync(
        TranscriptionJobRequest request,
        IProgress<EngineProgress>? progress,
        CancellationToken cancellationToken)
    {
        ConfigureRuntime(request.Device);

        Directory.CreateDirectory(_modelsDirectory);
        var modelPath = Path.Combine(_modelsDirectory, ModelManager.GetModelFileName(request.Quality));

        progress?.Report(new EngineProgress("loading"));

        var ensureModelTask = _modelEnsurer.EnsureModelAsync(modelPath, request.Quality, cancellationToken);
        var audioTask = Task.Run(() => _audioLoader.LoadSamples16kHz(request.MediaFilePath), cancellationToken);
        await ensureModelTask;
        var samples = await audioTask;

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new EngineProgress("preparing"));
        var silenceChunks = SilenceSplitter.SplitBySilence(samples);
        var (maxPartes, paralelismo, threadsPorJob) = ChunkPlanner.CalculateParallelLimits(request.Device);
        var partes = ChunkPlanner.GroupParts(silenceChunks, maxPartes);

        _factoryCache.GetOrCreate(modelPath);

        progress?.Report(new EngineProgress("transcribing", 0, partes.Count));

        var segmentos = new ConcurrentBag<(int ParteIndex, double InicioSec, double FimSec, string Texto)>();

        await Parallel.ForEachAsync(
            partes.Select((parte, index) => (Index: index, Parte: parte)),
            new ParallelOptions { MaxDegreeOfParallelism = paralelismo, CancellationToken = cancellationToken },
            async (item, ct) =>
            {
                var (index, (offset, chunk)) = (item.Index, item.Parte);
                progress?.Report(new EngineProgress("transcribing", index + 1, partes.Count));

                await using var processor = _processorFactory.CreateProcessor(
                    modelPath,
                    request.Language,
                    threadsPorJob);

                await foreach (var result in processor.ProcessAsync(chunk, ct))
                {
                    segmentos.Add((
                        index,
                        result.Start.TotalSeconds + offset,
                        result.End.TotalSeconds + offset,
                        result.Text.Trim()));
                }
            });

        var ordered = segmentos
            .OrderBy(s => s.ParteIndex)
            .ThenBy(s => s.InicioSec)
            .Select(s => new TranscriptionSegmentResult(s.InicioSec, s.FimSec, s.Texto))
            .ToList();

        progress?.Report(new EngineProgress("done", partes.Count, partes.Count));
        return new TranscriptionResult(ordered);
    }

    public void Dispose() => _factoryCache.Dispose();

    private static void ConfigureRuntime(ExecutionDevice device)
    {
        var deviceCode = device switch
        {
            ExecutionDevice.Cpu => "cpu",
            ExecutionDevice.Cuda => "cuda",
            ExecutionDevice.Vulkan => "vulkan",
            ExecutionDevice.Auto => "cuda",
            _ => "cpu",
        };

        RuntimeOptions.RuntimeLibraryOrder = deviceCode switch
        {
            "cpu" => [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx],
            "cuda" => [RuntimeLibrary.Cuda, RuntimeLibrary.Cuda12],
            "vulkan" => [RuntimeLibrary.Vulkan],
            _ => throw new InvalidOperationException($"Dispositivo desconhecido: {deviceCode}"),
        };
    }
}
