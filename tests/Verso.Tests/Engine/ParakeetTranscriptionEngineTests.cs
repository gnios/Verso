using System.Net;
using System.Net.Http;
using System.Text;
using NAudio.Wave;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;

namespace Verso.Tests.Engine;

public class ParakeetModelManagerTests
{
    [Fact]
    public void GetModelDirectoryName_MapsBothCatalogModels()
    {
        Assert.Equal("parakeet-tdt-0.6b-v3-int8", ParakeetModelManager.GetModelDirectoryName(ParakeetModel.MultilingualV3));
        Assert.Equal("parakeet-ptbr-tagarela-int8", ParakeetModelManager.GetModelDirectoryName(ParakeetModel.PtBrTagarela));
    }

    [Fact]
    public void IsModelDirectoryValid_RejectsMissingEncoder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"verso-pk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var file in ParakeetModelManager.RequiredFiles)
            {
                File.WriteAllText(Path.Combine(dir, file), "x");
            }

            Assert.False(ParakeetModelManager.IsModelDirectoryValid(dir, ParakeetModel.MultilingualV3));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsModelDirectoryValid_AcceptsCompleteDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"verso-pk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, ParakeetModelManager.DecoderJointFileName), "decoder");
            File.WriteAllText(Path.Combine(dir, ParakeetModelManager.PreprocessorFileName), "pre");
            File.WriteAllText(Path.Combine(dir, ParakeetModelManager.VocabFileName), "a 0");
            var encoder = Path.Combine(dir, ParakeetModelManager.EncoderFileName);
            using (var stream = File.Create(encoder))
            {
                stream.SetLength(ParakeetModelManager.GetMinimumEncoderBytes(ParakeetModel.MultilingualV3));
            }

            Assert.True(ParakeetModelManager.IsModelDirectoryValid(dir, ParakeetModel.MultilingualV3));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureModelAsync_WhenDirectoryIsValid_DoesNotDownloadOrNotify()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"verso-pk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var handler = new StubHttpHandler();
        var notifier = new RecordingDownloadNotifier();
        try
        {
            WriteCompleteModelDirectory(dir, ParakeetModel.MultilingualV3);
            var manager = new ParakeetModelManager(
                downloadNotifier: notifier,
                httpClient: new HttpClient(handler));

            await manager.EnsureModelAsync(dir, ParakeetModel.MultilingualV3);

            Assert.Empty(handler.RequestedUrls);
            Assert.Equal(0, notifier.StartedCount);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureModelAsync_WhenDirectoryIncomplete_DownloadsMissingFilesAndNotifies()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"verso-pk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var handler = new StubHttpHandler();
        var notifier = new RecordingDownloadNotifier();
        try
        {
            File.WriteAllText(Path.Combine(dir, ParakeetModelManager.VocabFileName), "a 0");
            using (var stream = File.Create(Path.Combine(dir, ParakeetModelManager.EncoderFileName)))
            {
                stream.SetLength(ParakeetModelManager.GetMinimumEncoderBytes(ParakeetModel.MultilingualV3));
            }

            var manager = new ParakeetModelManager(
                downloadNotifier: notifier,
                httpClient: new HttpClient(handler));

            await manager.EnsureModelAsync(dir, ParakeetModel.MultilingualV3);

            Assert.Equal(1, notifier.StartedCount);
            Assert.Equal(ParakeetModelManager.GetDisplayName(ParakeetModel.MultilingualV3), notifier.DisplayName);
            Assert.False(string.IsNullOrWhiteSpace(notifier.Detail));
            Assert.Equal(1, notifier.CompletedCount);
            Assert.Contains(handler.RequestedUrls, url => url.EndsWith(ParakeetModelManager.DecoderJointFileName, StringComparison.Ordinal));
            Assert.Contains(handler.RequestedUrls, url => url.EndsWith(ParakeetModelManager.PreprocessorFileName, StringComparison.Ordinal));
            Assert.DoesNotContain(handler.RequestedUrls, url => url.EndsWith(ParakeetModelManager.EncoderFileName, StringComparison.Ordinal));
            Assert.DoesNotContain(handler.RequestedUrls, url => url.EndsWith(ParakeetModelManager.VocabFileName, StringComparison.Ordinal));
            Assert.True(ParakeetModelManager.IsModelDirectoryValid(dir, ParakeetModel.MultilingualV3));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureModelAsync_WhenDirectoryMissing_DownloadsAllRequiredFilesAndNotifies()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"verso-pk-{Guid.NewGuid():N}");
        var handler = new StubHttpHandler();
        var notifier = new RecordingDownloadNotifier();
        try
        {
            var manager = new ParakeetModelManager(
                downloadNotifier: notifier,
                httpClient: new HttpClient(handler));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.EnsureModelAsync(dir, ParakeetModel.MultilingualV3));

            Assert.Contains("incompleto", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, notifier.StartedCount);
            Assert.Equal(ParakeetModelManager.GetDisplayName(ParakeetModel.MultilingualV3), notifier.DisplayName);
            Assert.Equal(1, notifier.CompletedCount);
            Assert.Equal(ParakeetModelManager.RequiredFiles.Length, handler.RequestedUrls.Count);
            foreach (var file in ParakeetModelManager.RequiredFiles)
            {
                Assert.Contains(handler.RequestedUrls, url => url.EndsWith(file, StringComparison.Ordinal));
            }
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static void WriteCompleteModelDirectory(string dir, ParakeetModel model)
    {
        File.WriteAllText(Path.Combine(dir, ParakeetModelManager.DecoderJointFileName), "decoder");
        File.WriteAllText(Path.Combine(dir, ParakeetModelManager.PreprocessorFileName), "pre");
        File.WriteAllText(Path.Combine(dir, ParakeetModelManager.VocabFileName), "a 0");
        using var stream = File.Create(Path.Combine(dir, ParakeetModelManager.EncoderFileName));
        stream.SetLength(ParakeetModelManager.GetMinimumEncoderBytes(model));
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];
        public Dictionary<string, byte[]> Bodies { get; } = new(StringComparer.Ordinal);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? "";
            RequestedUrls.Add(uri);
            var fileName = Path.GetFileName(request.RequestUri?.AbsolutePath ?? "");
            var body = Bodies.TryGetValue(fileName, out var bytes)
                ? bytes
                : Encoding.UTF8.GetBytes("ok");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            });
        }
    }

    private sealed class RecordingDownloadNotifier : IModelDownloadNotifier
    {
        public int StartedCount { get; private set; }
        public int CompletedCount { get; private set; }
        public string? DisplayName { get; private set; }
        public string? Detail { get; private set; }

        public void DownloadStarted(ModelQuality quality)
        {
        }

        public void DownloadStarted(string displayName, string detail)
        {
            StartedCount++;
            DisplayName = displayName;
            Detail = detail;
        }

        public void DownloadCompleted() => CompletedCount++;
    }
}

public class ParakeetTranscriptionEngineTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public async Task TranscribeAsync_WithFakeRecognizer_ProducesTimestampedSegments()
    {
        var wavPath = CreateTempWav(seconds: 1);
        var modelsDir = Path.Combine(Path.GetTempPath(), $"verso-pk-models-{Guid.NewGuid():N}");
        Directory.CreateDirectory(modelsDir);
        var engine = new ParakeetTranscriptionEngine(
            new AudioLoader(new FfmpegLocator()),
            new NoOpParakeetModelEnsurer(),
            new FakeParakeetRecognizerFactory(
            [
                new TranscriptionSegmentResult(0.0, 0.8, "olá mundo"),
            ]),
            logger: null,
            modelsDirectory: modelsDir);

        var result = await engine.TranscribeAsync(
            new TranscriptionJobRequest(
                Guid.NewGuid(),
                wavPath,
                "pt",
                ModelQuality.Standard,
                ExecutionDevice.Cpu,
                Engine: TranscriptionEngineKind.Parakeet,
                ParakeetModel: ParakeetModel.PtBrTagarela),
            progress: null,
            CancellationToken.None);

        Assert.Single(result.Segments);
        Assert.Equal("olá mundo", result.Segments[0].Text);
        Assert.Equal(0.0, result.Segments[0].StartSeconds);
        Assert.Equal(0.8, result.Segments[0].EndSeconds);
        Directory.Delete(modelsDir, recursive: true);
    }

    [Fact]
    public async Task TranscribeAsync_EnsuresModelBeforeRecognize()
    {
        var wavPath = CreateTempWav(seconds: 1);
        var modelsDir = Path.Combine(Path.GetTempPath(), $"verso-pk-models-{Guid.NewGuid():N}");
        Directory.CreateDirectory(modelsDir);
        var order = new List<string>();
        var engine = new ParakeetTranscriptionEngine(
            new AudioLoader(new FfmpegLocator()),
            new RecordingParakeetModelEnsurer(order),
            new RecordingParakeetRecognizerFactory(order),
            logger: null,
            modelsDirectory: modelsDir);

        await engine.TranscribeAsync(
            new TranscriptionJobRequest(
                Guid.NewGuid(),
                wavPath,
                "pt",
                ModelQuality.Standard,
                ExecutionDevice.Cpu,
                Engine: TranscriptionEngineKind.Parakeet),
            progress: null,
            CancellationToken.None);

        Assert.Equal(["ensure", "recognize"], order);
        Directory.Delete(modelsDir, recursive: true);
    }

    [Fact]
    public async Task TranscribeAsync_WhenRecognizerReturnsNoTokens_ReturnsEmptySegments()
    {
        var wavPath = CreateTempWav(seconds: 1);
        var modelsDir = Path.Combine(Path.GetTempPath(), $"verso-pk-models-{Guid.NewGuid():N}");
        Directory.CreateDirectory(modelsDir);
        var engine = new ParakeetTranscriptionEngine(
            new AudioLoader(new FfmpegLocator()),
            new NoOpParakeetModelEnsurer(),
            new FakeParakeetRecognizerFactory([]),
            logger: null,
            modelsDirectory: modelsDir);

        var result = await engine.TranscribeAsync(
            new TranscriptionJobRequest(
                Guid.NewGuid(),
                wavPath,
                "pt",
                ModelQuality.Standard,
                ExecutionDevice.Cuda,
                Engine: TranscriptionEngineKind.Parakeet),
            progress: null,
            CancellationToken.None);

        Assert.Empty(result.Segments);
        Directory.Delete(modelsDir, recursive: true);
    }

    [Fact]
    public async Task TranscribeAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var wavPath = CreateTempWav(seconds: 1);
        var modelsDir = Path.Combine(Path.GetTempPath(), $"verso-pk-models-{Guid.NewGuid():N}");
        Directory.CreateDirectory(modelsDir);
        var engine = new ParakeetTranscriptionEngine(
            new AudioLoader(new FfmpegLocator()),
            new NoOpParakeetModelEnsurer(),
            new FakeParakeetRecognizerFactory([]),
            logger: null,
            modelsDirectory: modelsDir);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.TranscribeAsync(
                new TranscriptionJobRequest(
                    Guid.NewGuid(),
                    wavPath,
                    "pt",
                    ModelQuality.Standard,
                    ExecutionDevice.Cpu,
                    Engine: TranscriptionEngineKind.Parakeet),
                progress: null,
                cts.Token));
        Directory.Delete(modelsDir, recursive: true);
    }

    [Fact]
    public async Task TranscribeAsync_LongAudio_DecodesPerWindowAndReportsProgress()
    {
        var wavPath = CreateTempWav(seconds: 50);
        var modelsDir = Path.Combine(Path.GetTempPath(), $"verso-pk-models-{Guid.NewGuid():N}");
        Directory.CreateDirectory(modelsDir);
        var factory = new CountingParakeetRecognizerFactory();
        var engine = new ParakeetTranscriptionEngine(
            new AudioLoader(new FfmpegLocator()),
            new NoOpParakeetModelEnsurer(),
            factory,
            logger: null,
            modelsDirectory: modelsDir);
        var progress = new RecordingEngineProgress();

        await engine.TranscribeAsync(
            new TranscriptionJobRequest(
                Guid.NewGuid(),
                wavPath,
                "pt",
                ModelQuality.Standard,
                ExecutionDevice.Cpu,
                Engine: TranscriptionEngineKind.Parakeet,
                ParakeetModel: ParakeetModel.PtBrTagarela),
            progress,
            CancellationToken.None);

        var expectedWindows = ParakeetAudioChunker.CountWindowsFromDuration(50);
        Assert.Equal(expectedWindows, factory.Recognizer.Calls);
        Assert.Contains(progress.Reports, e => e.Stage == "transcribing" && e.PartIndex == 0 && e.TotalParts == expectedWindows);
        Assert.Contains(progress.Reports, e => e.Stage == "transcribing" && e.PartIndex == expectedWindows && e.TotalParts == expectedWindows);
        Directory.Delete(modelsDir, recursive: true);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private string CreateTempWav(int seconds)
    {
        var path = Path.Combine(Path.GetTempPath(), $"verso-pk-{Guid.NewGuid():N}.wav");
        _tempFiles.Add(path);
        var sampleRate = 16000;
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1));
        var samples = new byte[sampleRate * 2 * seconds];
        writer.Write(samples, 0, samples.Length);
        return path;
    }

    private sealed class RecordingParakeetModelEnsurer(List<string> order) : IParakeetModelEnsurer
    {
        public Task EnsureModelAsync(string directory, ParakeetModel model, CancellationToken cancellationToken)
        {
            order.Add("ensure");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingParakeetRecognizerFactory(List<string> order) : IParakeetRecognizerFactory
    {
        public IParakeetRecognizer Create(string modelDirectory, int threads) =>
            new RecordingRecognizer(order);
    }

    private sealed class RecordingRecognizer(List<string> order) : IParakeetRecognizer
    {
        public IReadOnlyList<TranscriptionSegmentResult> Recognize(
            float[] samples16kHz,
            CancellationToken cancellationToken = default,
            IProgress<EngineProgress>? progress = null)
        {
            order.Add("recognize");
            return [new TranscriptionSegmentResult(0, 0.4, "ok")];
        }
    }

    private sealed class NoOpParakeetModelEnsurer : IParakeetModelEnsurer
    {
        public Task EnsureModelAsync(string directory, ParakeetModel model, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeParakeetRecognizerFactory(IReadOnlyList<TranscriptionSegmentResult> segments)
        : IParakeetRecognizerFactory
    {
        public IParakeetRecognizer Create(string modelDirectory, int threads) =>
            new FakeRecognizer(segments);
    }

    private sealed class FakeRecognizer(IReadOnlyList<TranscriptionSegmentResult> segments) : IParakeetRecognizer
    {
        public IReadOnlyList<TranscriptionSegmentResult> Recognize(
            float[] samples16kHz,
            CancellationToken cancellationToken = default,
            IProgress<EngineProgress>? progress = null) =>
            segments;
    }

    private sealed class CountingParakeetRecognizerFactory : IParakeetRecognizerFactory
    {
        public CountingRecognizer Recognizer { get; } = new();

        public IParakeetRecognizer Create(string modelDirectory, int threads) => Recognizer;
    }

    private sealed class CountingRecognizer : IParakeetRecognizer
    {
        public int Calls { get; private set; }

        public IReadOnlyList<TranscriptionSegmentResult> Recognize(
            float[] samples16kHz,
            CancellationToken cancellationToken = default,
            IProgress<EngineProgress>? progress = null)
        {
            Calls++;
            return [new TranscriptionSegmentResult(0, 0.4, "ok")];
        }
    }

    private sealed class RecordingEngineProgress : IProgress<EngineProgress>
    {
        public List<EngineProgress> Reports { get; } = [];

        public void Report(EngineProgress value) => Reports.Add(value);
    }
}

public class DispatchingTranscriptionEngineTests
{
    [Fact]
    public async Task TranscribeAsync_ParakeetJob_DoesNotCallWhisper()
    {
        var whisper = new TrackingEngine("whisper");
        var parakeet = new TrackingEngine("parakeet");
        var dispatcher = new DispatchingTranscriptionEngine(whisper, parakeet);

        var request = new TranscriptionJobRequest(
            Guid.NewGuid(),
            "a.wav",
            "pt",
            ModelQuality.Standard,
            ExecutionDevice.Cpu,
            Engine: TranscriptionEngineKind.Parakeet);

        var result = await dispatcher.TranscribeAsync(request, null, CancellationToken.None);
        Assert.True(parakeet.Called);
        Assert.False(whisper.Called);
        Assert.Equal("parakeet", result.Segments[0].Text);
    }

    [Fact]
    public async Task TranscribeAsync_WhisperJob_DoesNotCallParakeet()
    {
        var whisper = new TrackingEngine("whisper");
        var parakeet = new TrackingEngine("parakeet");
        var dispatcher = new DispatchingTranscriptionEngine(whisper, parakeet);

        var request = new TranscriptionJobRequest(
            Guid.NewGuid(),
            "a.wav",
            "pt",
            ModelQuality.Standard,
            ExecutionDevice.Cpu);

        var result = await dispatcher.TranscribeAsync(request, null, CancellationToken.None);
        Assert.True(whisper.Called);
        Assert.False(parakeet.Called);
        Assert.Equal("whisper", result.Segments[0].Text);
    }

    [Fact]
    public async Task TranscribeAsync_ParakeetJobWithCudaDevice_StillDispatchesToParakeet()
    {
        var whisper = new TrackingEngine("whisper");
        var parakeet = new TrackingEngine("parakeet");
        var dispatcher = new DispatchingTranscriptionEngine(whisper, parakeet);

        var request = new TranscriptionJobRequest(
            Guid.NewGuid(),
            "a.wav",
            "pt",
            ModelQuality.Standard,
            ExecutionDevice.Cuda,
            Engine: TranscriptionEngineKind.Parakeet);

        var result = await dispatcher.TranscribeAsync(request, null, CancellationToken.None);
        Assert.True(parakeet.Called);
        Assert.False(whisper.Called);
        Assert.Equal("parakeet", result.Segments[0].Text);
    }

    private sealed class TrackingEngine(string label) : ITranscriptionEngine
    {
        public bool Called { get; private set; }

        public Task<TranscriptionResult> TranscribeAsync(
            TranscriptionJobRequest request,
            IProgress<EngineProgress>? progress,
            CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(new TranscriptionResult(
                [new TranscriptionSegmentResult(0, 1, label)]));
        }
    }
}
