using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Verso.Core.Catalogs;
using Verso.Core.Data;
using Verso.Core.Data.Entities;
using Verso.Core.Engine.Worker;

namespace Verso.Core.Engine;

public sealed class TranscriptionQueueService : BackgroundService
{
    private readonly IDbContextFactory<VersoDbContext> _dbContextFactory;
    private readonly ITranscriptionEngine _engine;
    private readonly ILogger<TranscriptionQueueService> _logger;
    private readonly Channel<TranscriptionJobRequest> _channel;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobCancellationSources = new();
    private readonly TaskCompletionSource _startupCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task StartupCompleted => _startupCompleted.Task;

    public TranscriptionQueueService(
        IDbContextFactory<VersoDbContext> dbContextFactory,
        ITranscriptionEngine engine,
        ILogger<TranscriptionQueueService>? logger = null)
    {
        _dbContextFactory = dbContextFactory;
        _engine = engine;
        _logger = logger ?? NullLogger<TranscriptionQueueService>.Instance;
        _channel = Channel.CreateUnbounded<TranscriptionJobRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public event EventHandler<TranscriptionStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<TranscriptionProgressEventArgs>? ProgressChanged;

    public Guid Enqueue(TranscriptionJobRequest request)
    {
        if (!_channel.Writer.TryWrite(request))
            throw new InvalidOperationException("Não foi possível enfileirar a transcrição.");

        RaiseStatusChanged(request.TranscriptionId, TranscriptionStatusChanged.Queued);
        return request.TranscriptionId;
    }

    public void Cancel(Guid transcriptionId)
    {
        if (_jobCancellationSources.TryGetValue(transcriptionId, out var cts))
            cts.Cancel();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Escapa do SynchronizationContext do host (Photino/Blazor STA): sem isso,
        // continuations da fila podem rodar na UI thread e travar o front em 100%
        // enquanto PersistSuccessAsync grava no SQLite.
        await Task.Yield();

        try
        {
            await RecoverOrphanedJobsAsync(stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            _startupCompleted.TrySetResult();
        }

        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            await ProcessJobAsync(request, stoppingToken).ConfigureAwait(false);
    }

    private async Task RecoverOrphanedJobsAsync(CancellationToken cancellationToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var orphaned = await context.Transcriptions
            .Where(t => t.Status == TranscriptionStatus.InProgress)
            .ToListAsync(cancellationToken);

        if (orphaned.Count == 0)
            return;

        _logger.LogInformation(
            "Recuperando {Count} transcrição(ões) órfã(s) — status redefinido para Error",
            orphaned.Count);

        foreach (var transcription in orphaned)
        {
            transcription.Status = TranscriptionStatus.Error;
            transcription.ErrorMessage = "Interrompida";
            RaiseStatusChanged(transcription.Id, TranscriptionStatusChanged.Error, "Interrompida");
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessJobAsync(TranscriptionJobRequest request, CancellationToken stoppingToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _jobCancellationSources[request.TranscriptionId] = linkedCts;

        try
        {
            await UpdateStatusAsync(request.TranscriptionId, TranscriptionStatus.InProgress, null, stoppingToken)
                .ConfigureAwait(false);
            RaiseStatusChanged(request.TranscriptionId, TranscriptionStatusChanged.InProgress);
            // Progress síncrono (sem capturar SyncContext): System.Progress<T> postaria
            // no contexto da UI e poderia serializar/bloquear atualizações de status.
            var progress = new SynchronousProgress<EngineProgress>(e =>
                RaiseProgressChanged(request.TranscriptionId, e.Stage, e.PartIndex, e.TotalParts));
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation(
                "Transcrevendo {TranscriptionId}: dispositivo={Device}, modelo={Quality}",
                request.TranscriptionId,
                request.Device,
                request.Quality);
            var result = await _engine.TranscribeAsync(request, progress, linkedCts.Token)
                .ConfigureAwait(false);
            stopwatch.Stop();
            _logger.LogInformation(
                "Transcrição {TranscriptionId} concluída em {Seconds:F1}s — persistindo resultados…",
                request.TranscriptionId,
                stopwatch.Elapsed.TotalSeconds);
            // Whisper terminou; ainda falta gravar segmentos — UI não deve parecer “pronta”.
            RaiseProgressChanged(request.TranscriptionId, "saving", null, null);
            await PersistSuccessAsync(request, result, stopwatch.Elapsed.TotalSeconds, stoppingToken)
                .ConfigureAwait(false);

            RaiseStatusChanged(request.TranscriptionId, TranscriptionStatusChanged.Done);
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Transcrição {TranscriptionId} cancelada pelo usuário",
                request.TranscriptionId);
            await UpdateStatusAsync(request.TranscriptionId, TranscriptionStatus.Error, "Cancelada", stoppingToken);
            RaiseStatusChanged(request.TranscriptionId, TranscriptionStatusChanged.Error, "Cancelada");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro ao transcrever {TranscriptionId}: {ErrorMessage}",
                request.TranscriptionId,
                ex.Message);
            await UpdateStatusAsync(request.TranscriptionId, TranscriptionStatus.Error, ex.Message, stoppingToken);
            RaiseStatusChanged(request.TranscriptionId, TranscriptionStatusChanged.Error, ex.Message);
        }
        finally
        {
            _jobCancellationSources.TryRemove(request.TranscriptionId, out _);
        }
    }

    private async Task PersistSuccessAsync(
        TranscriptionJobRequest request,
        TranscriptionResult result,
        double processingSeconds,
        CancellationToken cancellationToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var transcription = await context.Transcriptions
            .SingleOrDefaultAsync(t => t.Id == request.TranscriptionId, cancellationToken);

        if (transcription is null)
        {
            throw new InvalidOperationException($"Transcrição {request.TranscriptionId} não encontrada.");
        }

        await context.Segments
            .Where(s => s.TranscriptionId == request.TranscriptionId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Speakers
            .Where(s => s.TranscriptionId == request.TranscriptionId)
            .ExecuteDeleteAsync(cancellationToken);

        Speaker? defaultSpeaker = null;
        if (transcription.SpeakerMode == SpeakerMode.Automatic)
        {
            defaultSpeaker = new Speaker
            {
                Id = Guid.NewGuid(),
                TranscriptionId = request.TranscriptionId,
                Name = "Locutor 1",
                ColorHex = SpeakerColorCatalog.ColorAtIndex(0),
            };
            context.Speakers.Add(defaultSpeaker);
        }

        var sortOrder = 0;
        foreach (var segment in result.Segments)
        {
            context.Segments.Add(new Segment
            {
                Id = Guid.NewGuid(),
                TranscriptionId = request.TranscriptionId,
                StartSeconds = segment.StartSeconds,
                EndSeconds = segment.EndSeconds,
                Text = segment.Text,
                SortOrder = sortOrder++,
                SpeakerId = defaultSpeaker?.Id,
            });
        }

        var duration = result.Segments.Count > 0 ? result.Segments.Max(s => s.EndSeconds) : 0d;
        var updated = await context.Transcriptions
            .Where(t => t.Id == request.TranscriptionId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.Status, TranscriptionStatus.Done)
                    .SetProperty(t => t.ErrorMessage, (string?)null)
                    .SetProperty(t => t.DurationSeconds, duration)
                    .SetProperty(t => t.ProcessingDurationSeconds, (double?)processingSeconds),
                cancellationToken);

        if (updated == 0)
            throw new InvalidOperationException($"Transcrição {request.TranscriptionId} não encontrada.");

        await context.SaveChangesAsync(cancellationToken);

        // Atualiza o cache de RTF para refinar estimativas futuras deste modelo+dispositivo
        if (duration > 0 && processingSeconds > 0)
        {
            var actualRtf = processingSeconds / duration;
            TranscriptionEstimator.RecordRtf(request.Quality, request.Device, actualRtf);
        }
    }

    private async Task UpdateStatusAsync(
        Guid transcriptionId,
        TranscriptionStatus status,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var updated = await context.Transcriptions
            .Where(t => t.Id == transcriptionId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.Status, status)
                    .SetProperty(t => t.ErrorMessage, errorMessage),
                cancellationToken);

        if (updated == 0)
        {
            _logger.LogWarning(
                "Transcrição {TranscriptionId} não encontrada ao atualizar status — pode ter sido excluída.",
                transcriptionId);
        }
    }

    private void RaiseStatusChanged(
        Guid transcriptionId,
        TranscriptionStatusChanged status,
        string? errorMessage = null) =>
        StatusChanged?.Invoke(this, new TranscriptionStatusChangedEventArgs(transcriptionId, status)
        {
            ErrorMessage = errorMessage,
        });

    private void RaiseProgressChanged(
        Guid transcriptionId,
        string stage,
        int? partIndex,
        int? totalParts) =>
        ProgressChanged?.Invoke(this, new TranscriptionProgressEventArgs(transcriptionId, stage, partIndex, totalParts));

    /// <summary>
    /// <see cref="IProgress{T}"/> síncrono — não captura <see cref="SynchronizationContext"/>,
    /// ao contrário de <see cref="Progress{T}"/>.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}

public static class EngineServiceCollectionExtensions
{
    /// <summary>
    /// Registra as dependências do motor Whisper (fábrica/processador/gestão de modelo), sem a
    /// fila de transcrição nem o registro de <see cref="ITranscriptionEngine"/>. Usado tanto pelo
    /// host in-process (<see cref="AddVersoEngine"/>) quanto pelo processo worker isolado
    /// (<c>Verso.Worker</c>), que resolve <see cref="WhisperTranscriptionEngine"/> diretamente.
    /// </summary>
    public static IServiceCollection AddWhisperEngine(this IServiceCollection services)
    {
        services.AddSingleton<FfmpegLocator>();
        services.AddSingleton<AudioLoader>();
        services.AddSingleton<ModelManager>();
        services.AddSingleton<IWhisperFactoryCache, WhisperFactoryCache>();
        services.AddSingleton<IWhisperProcessorFactory, WhisperProcessorFactory>();
        services.AddSingleton<WhisperTranscriptionEngine>();
        return services;
    }

    public static IServiceCollection AddVersoEngine(this IServiceCollection services)
    {
        services.AddWhisperEngine();
        services.AddSingleton<IWorkerExecutableLocator, WorkerExecutableLocator>();
        services.AddSingleton<IWorkerProcessFactory, WorkerProcessFactory>();
        services.AddSingleton<ITranscriptionEngine, WorkerProcessTranscriptionEngine>();
        services.AddSingleton<TranscriptionQueueService>();
        services.AddHostedService(sp => sp.GetRequiredService<TranscriptionQueueService>());
        return services;
    }
}
