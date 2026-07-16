using System.Text.Json;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Engine.Worker;

namespace Verso.Tests.Engine.Worker;

public class WorkerHostTests
{
    [Fact]
    public async Task RunAsync_HappyPath_WritesProgressPerReportThenResult()
    {
        var input = new StringReader(Serialize(new WorkerJobMessage(CreateRequest())) + "\n");
        var output = new StringWriter();
        var engine = new ProgressReportingInnerEngine();

        var exitCode = await new WorkerHost().RunAsync(input, output, engine, CancellationToken.None);

        var lines = SplitLines(output.ToString());
        Assert.Equal(0, exitCode);
        Assert.Equal(3, lines.Count);

        var progress1 = Assert.IsType<WorkerProgressMessage>(Deserialize(lines[0]));
        Assert.Equal("loading", progress1.Stage);

        var progress2 = Assert.IsType<WorkerProgressMessage>(Deserialize(lines[1]));
        Assert.Equal("transcribing", progress2.Stage);
        Assert.Equal(1, progress2.PartIndex);
        Assert.Equal(2, progress2.TotalParts);

        var resultMessage = Assert.IsType<WorkerResultMessage>(Deserialize(lines[2]));
        Assert.Single(resultMessage.Result.Segments);
        Assert.Equal("segmento ok", resultMessage.Result.Segments[0].Text);
    }

    [Fact]
    public async Task RunAsync_WhenEngineThrows_WritesErrorLine_WithoutPropagatingException()
    {
        var input = new StringReader(Serialize(new WorkerJobMessage(CreateRequest())) + "\n");
        var output = new StringWriter();
        var engine = new ThrowingInnerEngine("falha simulada");

        var exitCode = await new WorkerHost().RunAsync(input, output, engine, CancellationToken.None);

        Assert.Equal(1, exitCode);
        var lines = SplitLines(output.ToString());
        var errorMessage = Assert.IsType<WorkerErrorMessage>(Deserialize(Assert.Single(lines)));
        Assert.Equal("falha simulada", errorMessage.Message);
    }

    [Fact]
    public async Task RunAsync_WhenCancelLineReceived_CancelsInnerCancellationToken()
    {
        var input = new StringReader(
            Serialize(new WorkerJobMessage(CreateRequest())) + "\n" +
            Serialize(new WorkerCancelMessage()) + "\n");
        var output = new StringWriter();
        var engine = new CancellationObservingInnerEngine();

        await new WorkerHost().RunAsync(input, output, engine, CancellationToken.None);

        Assert.True(engine.ObservedToken.IsCancellationRequested);
    }

    private static TranscriptionJobRequest CreateRequest() =>
        new(Guid.NewGuid(), "sample.wav", "pt", ModelQuality.Standard, ExecutionDevice.Cpu);

    private static string Serialize(WorkerMessage message) =>
        JsonSerializer.Serialize<WorkerMessage>(message, WorkerProtocol.JsonOptions);

    private static WorkerMessage? Deserialize(string json) =>
        JsonSerializer.Deserialize<WorkerMessage>(json, WorkerProtocol.JsonOptions);

    private static List<string> SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private sealed class ProgressReportingInnerEngine : ITranscriptionEngine
    {
        public Task<TranscriptionResult> TranscribeAsync(
            TranscriptionJobRequest request,
            IProgress<EngineProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new EngineProgress("loading"));
            progress?.Report(new EngineProgress("transcribing", 1, 2));
            return Task.FromResult(new TranscriptionResult(
            [
                new TranscriptionSegmentResult(0, 1, "segmento ok"),
            ]));
        }
    }

    private sealed class ThrowingInnerEngine(string message) : ITranscriptionEngine
    {
        public Task<TranscriptionResult> TranscribeAsync(
            TranscriptionJobRequest request,
            IProgress<EngineProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);
    }

    private sealed class CancellationObservingInnerEngine : ITranscriptionEngine
    {
        public CancellationToken ObservedToken { get; private set; }

        public async Task<TranscriptionResult> TranscribeAsync(
            TranscriptionJobRequest request,
            IProgress<EngineProgress>? progress,
            CancellationToken cancellationToken)
        {
            ObservedToken = cancellationToken;
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new TranscriptionResult([]);
        }
    }
}
