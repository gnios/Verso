using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Verso.Core.Catalogs;
using Verso.Core.Data;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Engine.Diarization;
using Verso.Tests.Engine.Diarization;

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
    public async Task Enqueue_AutomaticTwoVoices_PersistsLocutor1And2WithCatalogColors()
    {
        var wavPath = WriteTwoToneWav();
        try
        {
            var engine = new FixedSegmentsEngine(
                new TranscriptionSegmentResult(0, 3, "fala um"),
                new TranscriptionSegmentResult(3, 6, "fala dois"));
            await using var fixture = await QueueFixture.CreateAsync(
                engine,
                new AudioLoader(new FfmpegLocator()),
                new LocalDiarizer(new PitchSplitSpeakerEmbedder()));
            var transcriptionId = await fixture.SeedTranscriptionAsync(SpeakerMode.Automatic, wavPath);

            fixture.Queue.Enqueue(fixture.CreateRequest(transcriptionId, wavPath));
            await fixture.WaitForStatusAsync(transcriptionId, TranscriptionStatus.Done);

            await using var context = await fixture.Factory.CreateDbContextAsync();
            var transcription = await context.Transcriptions
                .Include(t => t.Speakers)
                .Include(t => t.Segments)
                .SingleAsync(t => t.Id == transcriptionId);

            Assert.Equal(2, transcription.Speakers.Count);
            var locutor1 = transcription.Speakers.Single(s => s.Name == "Locutor 1");
            var locutor2 = transcription.Speakers.Single(s => s.Name == "Locutor 2");
            Assert.Equal(SpeakerColorCatalog.ColorAtIndex(0), locutor1.ColorHex);
            Assert.Equal(SpeakerColorCatalog.ColorAtIndex(1), locutor2.ColorHex);

            var ordered = transcription.Segments.OrderBy(s => s.SortOrder).ToList();
            Assert.Equal(2, ordered.Count);
            Assert.Equal(locutor1.Id, ordered[0].SpeakerId);
            Assert.Equal(locutor2.Id, ordered[1].SpeakerId);
            Assert.Equal("fala um", ordered[0].Text);
            Assert.Equal("fala dois", ordered[1].Text);
        }
        finally
        {
            File.Delete(wavPath);
        }
    }

    [Fact]
    public async Task Enqueue_SpeakerModeOff_PersistsAsrCutsWithoutSpeakers()
    {
        await using var fixture = await QueueFixture.CreateAsync(new SuccessTranscriptionEngine());
        var transcriptionId = await fixture.SeedTranscriptionAsync(SpeakerMode.Off);

        fixture.Queue.Enqueue(fixture.CreateRequest(transcriptionId));
        await fixture.WaitForStatusAsync(transcriptionId, TranscriptionStatus.Done);

        await using var context = await fixture.Factory.CreateDbContextAsync();
        var transcription = await context.Transcriptions
            .Include(t => t.Speakers)
            .Include(t => t.Segments)
            .SingleAsync(t => t.Id == transcriptionId);

        Assert.Empty(transcription.Speakers);
        Assert.Single(transcription.Segments);
        Assert.Null(transcription.Segments[0].SpeakerId);
        Assert.Equal("segmento ok", transcription.Segments[0].Text);
        Assert.Equal(0, transcription.Segments[0].StartSeconds);
        Assert.Equal(1.5, transcription.Segments[0].EndSeconds);
    }

    [Fact]
    public async Task Enqueue_AutomaticMissingMedia_CompletesWithLocutor1AndAsrCuts()
    {
        await using var fixture = await QueueFixture.CreateAsync(
            new SuccessTranscriptionEngine(),
            new AudioLoader(new FfmpegLocator()),
            new LocalDiarizer(new PitchSplitSpeakerEmbedder()));
        var transcriptionId = await fixture.SeedTranscriptionAsync();

        fixture.Queue.Enqueue(fixture.CreateRequest(transcriptionId));
        await fixture.WaitForStatusAsync(transcriptionId, TranscriptionStatus.Done);

        await using var context = await fixture.Factory.CreateDbContextAsync();
        var transcription = await context.Transcriptions
            .Include(t => t.Speakers)
            .Include(t => t.Segments)
            .SingleAsync(t => t.Id == transcriptionId);

        Assert.Equal(TranscriptionStatus.Done, transcription.Status);
        Assert.Single(transcription.Speakers);
        Assert.Equal("Locutor 1", transcription.Speakers[0].Name);
        Assert.Single(transcription.Segments);
        Assert.Equal("segmento ok", transcription.Segments[0].Text);
        Assert.Equal(0, transcription.Segments[0].StartSeconds);
        Assert.Equal(1.5, transcription.Segments[0].EndSeconds);
        Assert.Equal(transcription.Speakers[0].Id, transcription.Segments[0].SpeakerId);
    }

    [Fact]
    public async Task Enqueue_AutomaticDiarizerThrows_CompletesWithLocutor1()
    {
        var wavPath = WriteSilenceWav();
        try
        {
            await using var fixture = await QueueFixture.CreateAsync(
                new SuccessTranscriptionEngine(),
                new AudioLoader(new FfmpegLocator()),
                new ThrowingDiarizer());
            var transcriptionId = await fixture.SeedTranscriptionAsync(SpeakerMode.Automatic, wavPath);

            fixture.Queue.Enqueue(fixture.CreateRequest(transcriptionId, wavPath));
            await fixture.WaitForStatusAsync(transcriptionId, TranscriptionStatus.Done);

            await using var context = await fixture.Factory.CreateDbContextAsync();
            var transcription = await context.Transcriptions
                .Include(t => t.Speakers)
                .Include(t => t.Segments)
                .SingleAsync(t => t.Id == transcriptionId);

            Assert.Equal(TranscriptionStatus.Done, transcription.Status);
            Assert.Single(transcription.Speakers);
            Assert.Equal("Locutor 1", transcription.Speakers[0].Name);
            Assert.Equal("segmento ok", transcription.Segments[0].Text);
            Assert.Equal(0, transcription.Segments[0].StartSeconds);
            Assert.Equal(1.5, transcription.Segments[0].EndSeconds);
        }
        finally
        {
            File.Delete(wavPath);
        }
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
    public async Task HasActiveWork_IsTrueWhileJobRunsThenFalse()
    {
        var gate = new TaskCompletionSource();
        var reached = new TaskCompletionSource();
        await using var fixture = await QueueFixture.CreateAsync(new GatedProgressEngine(gate.Task, reached));
        var transcriptionId = await fixture.SeedTranscriptionAsync();

        Assert.False(fixture.Queue.HasActiveWork);
        fixture.Queue.Enqueue(fixture.CreateRequest(transcriptionId));
        Assert.True(fixture.Queue.HasActiveWork);

        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fixture.Queue.HasActiveWork);

        gate.SetResult();
        await fixture.WaitForStatusAsync(transcriptionId, TranscriptionStatus.Done);
        Assert.False(fixture.Queue.HasActiveWork);
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
        Assert.Contains(received, e => e.Stage == "diarizing");
        Assert.Contains(received, e => e.Stage == "saving");
        var diarizingAt = received.FindIndex(e => e.Stage == "diarizing");
        var savingAt = received.FindIndex(e => e.Stage == "saving");
        Assert.True(savingAt > diarizingAt, "diarizing deve anteceder saving");
        Assert.All(received, e => Assert.Equal(transcriptionId, e.TranscriptionId));
    }

    [Fact]
    public async Task Cancel_WhenJobIsRunning_UpdatesStatusToErrorCancelada()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new GatedProgressEngine(release.Task, started);
        await using var fixture = await QueueFixture.CreateAsync(engine);
        var transcriptionId = await fixture.SeedTranscriptionAsync();

        fixture.Queue.Enqueue(fixture.CreateRequest(transcriptionId));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        fixture.Queue.Cancel(transcriptionId);
        release.SetResult();

        await fixture.WaitForStatusAsync(transcriptionId, TranscriptionStatus.Error);

        await using var context = await fixture.Factory.CreateDbContextAsync();
        var transcription = await context.Transcriptions.SingleAsync(t => t.Id == transcriptionId);
        Assert.Equal(TranscriptionStatus.Error, transcription.Status);
        Assert.Equal("Cancelada", transcription.ErrorMessage);
    }

    [Fact]
    public async Task Cancel_WhenJobIsStillQueued_MarksCanceladaWithoutRunningEngine()
    {
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new CountingGatedEngine(releaseFirst.Task, firstStarted);
        await using var fixture = await QueueFixture.CreateAsync(engine);

        var firstId = await fixture.SeedTranscriptionAsync();
        var secondId = await fixture.SeedTranscriptionAsync();

        fixture.Queue.Enqueue(fixture.CreateRequest(firstId));
        fixture.Queue.Enqueue(fixture.CreateRequest(secondId));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        fixture.Queue.Cancel(secondId);
        releaseFirst.SetResult();

        await fixture.WaitForStatusAsync(firstId, TranscriptionStatus.Done);
        await fixture.WaitForStatusAsync(secondId, TranscriptionStatus.Error);

        await using var context = await fixture.Factory.CreateDbContextAsync();
        var second = await context.Transcriptions.SingleAsync(t => t.Id == secondId);
        Assert.Equal(TranscriptionStatus.Error, second.Status);
        Assert.Equal("Cancelada", second.ErrorMessage);
        Assert.Equal(1, engine.TranscribeCallCount);
    }

    [Fact]
    public async Task TryGetLatestProgress_KeepsSnapshotWhileRunning_AndClearsWhenDone()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reachedTranscribing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new GatedProgressEngine(release.Task, reachedTranscribing);
        await using var fixture = await QueueFixture.CreateAsync(engine);
        var transcriptionId = await fixture.SeedTranscriptionAsync();

        fixture.Queue.Enqueue(fixture.CreateRequest(transcriptionId));
        await reachedTranscribing.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(fixture.Queue.TryGetLatestProgress(transcriptionId, out var snapshot));
        Assert.Equal("transcribing", snapshot.Stage);
        Assert.Equal(1, snapshot.PartIndex);
        Assert.Equal(3, snapshot.TotalParts);
        Assert.Equal(33, snapshot.Percent);

        release.SetResult();
        await fixture.WaitForStatusAsync(transcriptionId, TranscriptionStatus.Done);

        Assert.False(fixture.Queue.TryGetLatestProgress(transcriptionId, out _));
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

        public static async Task<QueueFixture> CreateAsync(
            ITranscriptionEngine engine,
            AudioLoader? audioLoader = null,
            ILocalDiarizer? diarizer = null)
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
                NullLogger<TranscriptionQueueService>.Instance,
                audioLoader,
                diarizer);
            await queue.StartAsync(CancellationToken.None);
            await queue.StartupCompleted;

            return new QueueFixture
            {
                Queue = queue,
                Factory = factory,
                DbPath = dbPath,
            };
        }

        public async Task<Guid> SeedTranscriptionAsync(
            SpeakerMode speakerMode = SpeakerMode.Automatic,
            string mediaFilePath = "sample.wav")
        {
            var id = Guid.NewGuid();
            await using var context = await Factory.CreateDbContextAsync();
            context.Transcriptions.Add(new Transcription
            {
                Id = id,
                Title = "teste",
                Status = TranscriptionStatus.InProgress,
                CreatedAt = DateTime.UtcNow,
                MediaFilePath = mediaFilePath,
                Language = "pt",
                SpeakerMode = speakerMode,
            });
            await context.SaveChangesAsync();
            return id;
        }

        public TranscriptionJobRequest CreateRequest(Guid transcriptionId, string mediaFilePath = "sample.wav") =>
            new(
                transcriptionId,
                MediaFilePath: mediaFilePath,
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

    private static string WriteTwoToneWav()
    {
        var audio = Concat(
            GenerateTone(3, 120, 0.7f),
            GenerateTone(3, 220, 0.7f));
        return WriteMono16Wav(audio);
    }

    private static string WriteSilenceWav() => WriteMono16Wav(new float[AudioLoader.SampleRate]);

    private static float[] Concat(params float[][] parts)
    {
        var audio = new float[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, audio, offset, part.Length);
            offset += part.Length;
        }

        return audio;
    }

    private static float[] GenerateTone(double seconds, float frequencyHz, float amplitude)
    {
        var length = (int)(seconds * AudioLoader.SampleRate);
        var audio = new float[length];
        for (var i = 0; i < length; i++)
        {
            audio[i] = amplitude * MathF.Sin(2 * MathF.PI * frequencyHz * i / AudioLoader.SampleRate);
        }

        return audio;
    }

    private static string WriteMono16Wav(float[] samples)
    {
        var path = Path.Combine(Path.GetTempPath(), $"verso-dia-{Guid.NewGuid():N}.wav");
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataLength = samples.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(AudioLoader.SampleRate);
        writer.Write(AudioLoader.SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        foreach (var sample in samples)
        {
            var pcm = (short)Math.Clamp((int)MathF.Round(sample * 32767f), short.MinValue, short.MaxValue);
            writer.Write(pcm);
        }

        return path;
    }

    private sealed class FixedSegmentsEngine(params TranscriptionSegmentResult[] segments) : ITranscriptionEngine
    {
        public Task<TranscriptionResult> TranscribeAsync(
            TranscriptionJobRequest request,
            IProgress<EngineProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TranscriptionResult(segments));
    }

    private sealed class ThrowingDiarizer : ILocalDiarizer
    {
        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public IReadOnlyList<TranscriptionSegmentResult> Diarize(
            float[] audio,
            IReadOnlyList<TranscriptionSegmentResult> asrSegments) =>
            throw new InvalidOperationException("falha diarizacao");
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

    private sealed class GatedProgressEngine(
        Task release,
        TaskCompletionSource reachedTranscribing) : ITranscriptionEngine
    {
        public async Task<TranscriptionResult> TranscribeAsync(
            TranscriptionJobRequest request,
            IProgress<EngineProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new EngineProgress("loading"));
            progress?.Report(new EngineProgress("transcribing", 1, 3));
            reachedTranscribing.TrySetResult();
            await release.WaitAsync(cancellationToken);
            progress?.Report(new EngineProgress("done", 3, 3));
            return new TranscriptionResult([new TranscriptionSegmentResult(0, 1, "ok")]);
        }
    }

    private sealed class CountingGatedEngine(
        Task release,
        TaskCompletionSource firstStarted) : ITranscriptionEngine
    {
        private int _callCount;
        public int TranscribeCallCount => Volatile.Read(ref _callCount);

        public async Task<TranscriptionResult> TranscribeAsync(
            TranscriptionJobRequest request,
            IProgress<EngineProgress>? progress,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);
            if (call == 1)
            {
                firstStarted.TrySetResult();
                await release.WaitAsync(cancellationToken);
            }

            return new TranscriptionResult([new TranscriptionSegmentResult(0, 1, "ok")]);
        }
    }
}
