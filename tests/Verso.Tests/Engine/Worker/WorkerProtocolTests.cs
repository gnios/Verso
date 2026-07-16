using System.Text.Json;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Engine.Worker;

namespace Verso.Tests.Engine.Worker;

public class WorkerProtocolTests
{
    [Fact]
    public void WorkerJobMessage_RoundTrips_AndDiscriminatorIsJob()
    {
        var request = new TranscriptionJobRequest(
            Guid.NewGuid(),
            "sample.wav",
            "pt",
            ModelQuality.Standard,
            ExecutionDevice.Cpu,
            MaxTranscriptionThreads: 4);
        var message = new WorkerJobMessage(request);

        var json = JsonSerializer.Serialize<WorkerMessage>(message, WorkerProtocol.JsonOptions);
        Assert.Contains("\"type\":\"job\"", json);

        var deserialized = JsonSerializer.Deserialize<WorkerMessage>(json, WorkerProtocol.JsonOptions);
        var typed = Assert.IsType<WorkerJobMessage>(deserialized);
        Assert.Equal(request, typed.Request);
    }

    [Fact]
    public void WorkerCancelMessage_RoundTrips_AndDiscriminatorIsCancel()
    {
        var message = new WorkerCancelMessage();

        var json = JsonSerializer.Serialize<WorkerMessage>(message, WorkerProtocol.JsonOptions);
        Assert.Contains("\"type\":\"cancel\"", json);

        var deserialized = JsonSerializer.Deserialize<WorkerMessage>(json, WorkerProtocol.JsonOptions);
        Assert.IsType<WorkerCancelMessage>(deserialized);
    }

    [Fact]
    public void WorkerProgressMessage_RoundTrips_AndDiscriminatorIsProgress()
    {
        var message = new WorkerProgressMessage("transcribing", 2, 5);

        var json = JsonSerializer.Serialize<WorkerMessage>(message, WorkerProtocol.JsonOptions);
        Assert.Contains("\"type\":\"progress\"", json);

        var deserialized = JsonSerializer.Deserialize<WorkerMessage>(json, WorkerProtocol.JsonOptions);
        var typed = Assert.IsType<WorkerProgressMessage>(deserialized);
        Assert.Equal("transcribing", typed.Stage);
        Assert.Equal(2, typed.PartIndex);
        Assert.Equal(5, typed.TotalParts);
    }

    [Fact]
    public void WorkerResultMessage_RoundTrips_AndDiscriminatorIsResult()
    {
        var result = new TranscriptionResult(
        [
            new TranscriptionSegmentResult(0, 1.5, "segmento ok"),
        ]);
        var message = new WorkerResultMessage(result);

        var json = JsonSerializer.Serialize<WorkerMessage>(message, WorkerProtocol.JsonOptions);
        Assert.Contains("\"type\":\"result\"", json);

        var deserialized = JsonSerializer.Deserialize<WorkerMessage>(json, WorkerProtocol.JsonOptions);
        var typed = Assert.IsType<WorkerResultMessage>(deserialized);
        Assert.Single(typed.Result.Segments);
        Assert.Equal(0, typed.Result.Segments[0].StartSeconds);
        Assert.Equal(1.5, typed.Result.Segments[0].EndSeconds);
        Assert.Equal("segmento ok", typed.Result.Segments[0].Text);
    }

    [Fact]
    public void WorkerErrorMessage_RoundTrips_AndDiscriminatorIsError()
    {
        var message = new WorkerErrorMessage("falha simulada");

        var json = JsonSerializer.Serialize<WorkerMessage>(message, WorkerProtocol.JsonOptions);
        Assert.Contains("\"type\":\"error\"", json);

        var deserialized = JsonSerializer.Deserialize<WorkerMessage>(json, WorkerProtocol.JsonOptions);
        var typed = Assert.IsType<WorkerErrorMessage>(deserialized);
        Assert.Equal("falha simulada", typed.Message);
    }
}
