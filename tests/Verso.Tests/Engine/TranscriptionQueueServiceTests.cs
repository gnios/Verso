using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Verso.Core.Data;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;

namespace Verso.Tests.Engine;

public class TranscriptionQueueServiceTests
{
    [Fact]
    public async Task Enqueue_WhenEngineSucceeds_UpdatesStatusToDone()
    {
        await using var fixture = await QueueFixture.CreateAsync(new SuccessTranscriptionEngine());
        var transcriptionId = await fixture.SeedTranscriptionAsync();

        fixture.Queue.Enqueue(fixture.CreateRequest(transcriptionId));
        await fixture.WaitForStatusAsync(transcriptionId, TranscriptionStatus.Done);

        await using var context = await fixture.Factory.CreateDbContextAsync();
        var transcription = await context.Transcriptions
            .Include(t => t.Segments)
            .SingleAsync(t => t.Id == transcriptionId);
        Assert.Equal(TranscriptionStatus.Done, transcription.Status);
        Assert.Null(transcription.ErrorMessage);
        Assert.Equal("segmento ok", transcription.Segments[0].Text);
        Assert.True(transcription.ProcessingDurationSeconds.HasValue);
        Assert.True(transcription.ProcessingDurationSeconds!.Value > 0);
    }

    [Fact]
    public async Task Enqueue_WhenEngineThrows_UpdatesStatusToError()
    {
        await using var fixture = await QueueFixture.CreateAsync(new FailingTranscriptionEngine("falha simulada"));
        var transcriptionId = await fixture.SeedTranscriptionAsync();

        fixture.Queue.Enqueue(fixture.CreateRequest(transcriptionId));
        await fixture.WaitForStatusAsync(transcriptionId, TranscriptionStatus.Error);

        await using var context = await fixture.Factory.CreateDbContextAsync();
        var transcription = await context.Transcriptions.SingleAsync(t => t.Id == transcriptionId);

        Assert.Equal(TranscriptionStatus.Error, transcription.Status);
        Assert.Equal("falha simulada", transcription.ErrorMessage);
    }

    [Fact]
    public async Task Enqueue_TwoJobs_ProcessesSeriallyNeverConcurrently()
    {
        var trackingEngine = new TrackingTranscriptionEngine();
        await using var fixture = await QueueFixture.CreateAsync(trackingEngine);
        var firstId = await fixture.SeedTranscriptionAsync();
        var secondId = await fixture.SeedTranscriptionAsync();

        fixture.Queue.Enqueue(fixture.CreateRequest(firstId));
        fixture.Queue.Enqueue(fixture.CreateRequest(secondId));

        await fixture.WaitForStatusAsync(firstId, TranscriptionStatus.Done);
        await fixture.WaitForStatusAsync(secondId, TranscriptionStatus.Done);

        Assert.Equal(1, trackingEngine.MaxConcurrent);
    }

    [Fact]
    public async Task Startup_WhenOrphanInProgressExists_MarksAsErrorInterrompida()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            var services = new ServiceCollection();
            services.AddVersoDatabase(dbPath);
            var provider = services.BuildServiceProvider();
            await DbBootstrapper.MigrateAsync(provider);

            var factory = provider.GetRequiredService<IDbContextFactory<VersoDbContext>>();
            var orphanId = Guid.NewGuid();
            await using (var context = await factory.CreateDbContextAsync())
            {
                context.Transcriptions.Add(new Transcription
                {
                    Id = orphanId,
                    Title = "orphan",
                    Status = TranscriptionStatus.InProgress,
                    CreatedAt = DateTime.UtcNow,
                });
                await context.SaveChangesAsync();
            }

            var queue = new TranscriptionQueueService(
                factory,
                new SuccessTranscriptionEngine(),
                NullLogger<TranscriptionQueueService>.Instance);
            await queue.StartAsync(CancellationToken.None);
            await Task.Delay(300);
            await queue.StopAsync(CancellationToken.None);

            await using var readContext = await factory.CreateDbContextAsync();
            var transcription = await readContext.Transcriptions.SingleAsync(t => t.Id == orphanId);

            Assert.Equal(TranscriptionStatus.Error, transcription.Status);
            Assert.Equal("Interrompida", transcription.ErrorMessage);
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public async Task Enqueue_ForwardsEngineProgressViaProgressChangedEvent()
    {
        var engine = new ProgressReportingEngine();
        await using var fixture = await QueueFixture.CreateAsync(engine);
        var transcriptionId = await fixture.SeedTranscriptionAsync();

        var received = new List<TranscriptionProgressEventArgs>();
        fixture.Queue.ProgressChanged += (_, e) => received.Add(e);

        fixture.Queue.Enqueue(fixture.CreateRequest(transcriptionId));
        await fixture.WaitForStatusAsync(transcriptionId, TranscriptionStatus.Done);

        Assert.Contains(received, e => e.Stage == "loading");
        Assert.Contains(received, e => e.Stage == "transcribing" && e.PartIndex == 1 && e.TotalParts == 3);
        Assert.Contains(received, e => e.Percent == 67);
        Assert.All(received, e => Assert.Equal(transcriptionId, e.TranscriptionId));
    }

    private static string CreateTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"transcriba-queue-{Guid.NewGuid():N}", "verso.db");

    private static void CleanupDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private sealed class QueueFixture : IAsyncDisposable
    {
        public required TranscriptionQueueService Queue { get; init; }
        public required IDbContextFactory<VersoDbContext> Factory { get; init; }
        public required string DbPath { get; init; }

        public static async Task<QueueFixture> CreateAsync(ITranscriptionEngine engine)
        {
            var dbPath = CreateTempDbPath();
            var services = new ServiceCollection();
            services.AddVersoDatabase(dbPath);
            var provider = services.BuildServiceProvider();
            await DbBootstrapper.MigrateAsync(provider);

            var factory = provider.GetRequiredService<IDbContextFactory<VersoDbContext>>();
            var queue = new TranscriptionQueueService(
                factory,
                engine,
                NullLogger<TranscriptionQueueService>.Instance);
            await queue.StartAsync(CancellationToken.None);
            await queue.StartupCompleted;

            return new QueueFixture
            {
                Queue = queue,
                Factory = factory,
                DbPath = dbPath,
            };
        }

        public async Task<Guid> SeedTranscriptionAsync()
        {
            var id = Guid.NewGuid();
            await using var context = await Factory.CreateDbContextAsync();
            context.Transcriptions.Add(new Transcription
            {
                Id = id,
                Title = "teste",
                Status = TranscriptionStatus.InProgress,
                CreatedAt = DateTime.UtcNow,
                MediaFilePath = "sample.wav",
                Language = "pt",
            });
            await context.SaveChangesAsync();
            return id;
        }

        public TranscriptionJobRequest CreateRequest(Guid transcriptionId) =>
            new(
                transcriptionId,
                MediaFilePath: "sample.wav",
                Language: "pt",
                Quality: ModelQuality.Standard,
                Device: ExecutionDevice.Cpu);

        public async Task WaitForStatusAsync(Guid transcriptionId, TranscriptionStatus expectedStatus)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                await using var context = await Factory.CreateDbContextAsync();
                var status = await context.Transcriptions
                    .Where(t => t.Id == transcriptionId)
                    .Select(t => t.Status)
                    .SingleAsync();

                if (status == expectedStatus)
                    return;

                await Task.Delay(50);
            }

            await using var finalContext = await Factory.CreateDbContextAsync();
            var finalStatus = await finalContext.Transcriptions
                .Where(t => t.Id == transcriptionId)
                .Select(t => t.Status)
                .SingleAsync();
            Assert.Equal(expectedStatus, finalStatus);
        }

        public async ValueTask DisposeAsync()
        {
            await Queue.StopAsync(CancellationToken.None);
            CleanupDb(DbPath);
        }
    }

    private sealed class SuccessTranscriptionEngine : ITranscriptionEngine
    {
        public Task<TranscriptionResult> TranscribeAsync(
            TranscriptionJobRequest request,
            IProgress<EngineProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new TranscriptionResult(
            [
                new TranscriptionSegmentResult(0, 1.5, "segmento ok"),
            ]));
        }
    }

    private sealed class FailingTranscriptionEngine(string message) : ITranscriptionEngine
    {
        public Task<TranscriptionResult> TranscribeAsync(
            TranscriptionJobRequest request,
            IProgress<EngineProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);
    }

    private sealed class TrackingTranscriptionEngine : ITranscriptionEngine
    {
        private int _running;
        public int MaxConcurrent { get; private set; }

        public async Task<TranscriptionResult> TranscribeAsync(
            TranscriptionJobRequest request,
            IProgress<EngineProgress>? progress,
            CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _running);
            MaxConcurrent = Math.Max(MaxConcurrent, current);
            try
            {
                await Task.Delay(300, cancellationToken);
                return new TranscriptionResult(
                [
                    new TranscriptionSegmentResult(0, 1, "ok"),
                ]);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }

    private sealed class ProgressReportingEngine : ITranscriptionEngine
    {
        public async Task<TranscriptionResult> TranscribeAsync(
            TranscriptionJobRequest request,
            IProgress<EngineProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new EngineProgress("loading"));
            await Task.Yield();
            progress?.Report(new EngineProgress("transcribing", 1, 3));
            await Task.Yield();
            progress?.Report(new EngineProgress("transcribing", 2, 3));
            await Task.Yield();
            progress?.Report(new EngineProgress("done", 3, 3));
            return new TranscriptionResult([new TranscriptionSegmentResult(0, 1, "ok")]);
        }
    }
}
