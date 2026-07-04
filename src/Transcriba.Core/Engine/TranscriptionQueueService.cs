using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Transcriba.Core.Data;
using Transcriba.Core.Data.Entities;

namespace Transcriba.Core.Engine;

public sealed class TranscriptionQueueService : BackgroundService
{
    private readonly IDbContextFactory<TranscribaDbContext> _dbContextFactory;
    private readonly ITranscriptionEngine _engine;
    private readonly Channel<TranscriptionJobRequest> _channel;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobCancellationSources = new();
    private readonly TaskCompletionSource _startupCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task StartupCompleted => _startupCompleted.Task;

    public TranscriptionQueueService(
        IDbContextFactory<TranscribaDbContext> dbContextFactory,
        ITranscriptionEngine engine)
    {
        _dbContextFactory = dbContextFactory;
        _engine = engine;
        _channel = Channel.CreateUnbounded<TranscriptionJobRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public event EventHandler<TranscriptionStatusChangedEventArgs>? StatusChanged;

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
        try
        {
            await RecoverOrphanedJobsAsync(stoppingToken);
        }
        finally
        {
            _startupCompleted.TrySetResult();
        }

        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
            await ProcessJobAsync(request, stoppingToken);
    }

    private async Task RecoverOrphanedJobsAsync(CancellationToken cancellationToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var orphaned = await context.Transcriptions
            .Where(t => t.Status == TranscriptionStatus.InProgress)
            .ToListAsync(cancellationToken);

        if (orphaned.Count == 0)
            return;

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
            await UpdateStatusAsync(request.TranscriptionId, TranscriptionStatus.InProgress, null, stoppingToken);
            RaiseStatusChanged(request.TranscriptionId, TranscriptionStatusChanged.InProgress);

            var result = await _engine.TranscribeAsync(request, progress: null, linkedCts.Token);
            await PersistSuccessAsync(request, result, stoppingToken);

            RaiseStatusChanged(request.TranscriptionId, TranscriptionStatusChanged.Done);
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            await UpdateStatusAsync(request.TranscriptionId, TranscriptionStatus.Error, "Cancelada", stoppingToken);
            RaiseStatusChanged(request.TranscriptionId, TranscriptionStatusChanged.Error, "Cancelada");
        }
        catch (Exception ex)
        {
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
        CancellationToken cancellationToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        await context.Segments
            .Where(s => s.TranscriptionId == request.TranscriptionId)
            .ExecuteDeleteAsync(cancellationToken);

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
            });
        }

        var duration = result.Segments.Count > 0 ? result.Segments.Max(s => s.EndSeconds) : 0d;
        var updated = await context.Transcriptions
            .Where(t => t.Id == request.TranscriptionId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.Status, TranscriptionStatus.Done)
                    .SetProperty(t => t.ErrorMessage, (string?)null)
                    .SetProperty(t => t.DurationSeconds, duration),
                cancellationToken);

        if (updated == 0)
            throw new InvalidOperationException($"Transcrição {request.TranscriptionId} não encontrada.");

        await context.SaveChangesAsync(cancellationToken);
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
            throw new InvalidOperationException($"Transcrição {transcriptionId} não encontrada.");
    }

    private void RaiseStatusChanged(
        Guid transcriptionId,
        TranscriptionStatusChanged status,
        string? errorMessage = null) =>
        StatusChanged?.Invoke(this, new TranscriptionStatusChangedEventArgs(transcriptionId, status)
        {
            ErrorMessage = errorMessage,
        });
}

public static class EngineServiceCollectionExtensions
{
    public static IServiceCollection AddTranscribaEngine(this IServiceCollection services)
    {
        services.AddSingleton<FfmpegLocator>();
        services.AddSingleton<AudioLoader>();
        services.AddSingleton<ModelManager>();
        services.AddSingleton<IWhisperFactoryCache, WhisperFactoryCache>();
        services.AddSingleton<IWhisperProcessorFactory, WhisperProcessorFactory>();
        services.AddSingleton<WhisperTranscriptionEngine>();
        services.AddSingleton<ITranscriptionEngine, WhisperTranscriptionEngineAdapter>();
        services.AddSingleton<TranscriptionQueueService>();
        services.AddHostedService(sp => sp.GetRequiredService<TranscriptionQueueService>());
        return services;
    }
}
