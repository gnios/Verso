using Verso.Core.Engine.Diarization;

namespace Verso.Tests.Engine.Diarization;

public class SpeakerTurnAssemblerTests
{
    [Fact]
    public void ClusterTwo_OrthogonalVectors_AreSeparableIntoTwoLabels()
    {
        var embeddings = new[]
        {
            new float[] { 1, 0 },
            new float[] { 1, 0 },
            new float[] { 0, 1 },
        };

        var labels = SpeakerTurnAssembler.ClusterTwo(embeddings, out var separable);

        Assert.True(separable);
        Assert.Equal(labels[0], labels[1]);
        Assert.NotEqual(labels[0], labels[2]);
    }

    [Fact]
    public void ClusterTwo_IdenticalVectors_AreNotSeparable()
    {
        var embeddings = new[]
        {
            new float[] { 0.8f, 0.2f },
            new float[] { 0.81f, 0.19f },
        };

        var labels = SpeakerTurnAssembler.ClusterTwo(embeddings, out var separable);

        Assert.False(separable);
        Assert.All(labels, l => Assert.Equal(0, l));
    }
}

public class EcapaSpeakerModelManagerTests
{
    [Fact]
    public void GetModelPath_UsesDedicatedDirectory()
    {
        var path = EcapaSpeakerModelManager.GetModelPath(@"C:\models");

        Assert.EndsWith(
            Path.Combine("ecapa-tdnn-onnx", "ecapa_tdnn.onnx"),
            path);
    }

    [Fact]
    public void DownloadUrl_PinsHuggingFaceRevision()
    {
        Assert.Contains("pranjal-pravesh/ecapa_tdnn_onnx", EcapaSpeakerModelManager.DownloadUrl);
        Assert.Contains(EcapaSpeakerModelManager.Revision, EcapaSpeakerModelManager.DownloadUrl);
        Assert.EndsWith("ecapa_tdnn.onnx", EcapaSpeakerModelManager.DownloadUrl);
    }

    [Fact]
    public void IsModelValid_MissingFile_IsFalse()
    {
        Assert.False(EcapaSpeakerModelManager.IsModelValid(Path.Combine(Path.GetTempPath(), "missing-ecapa.onnx")));
    }

    [Fact]
    public void WaveformTensor_RankTwo_IsBatchByTime()
    {
        var samples = new float[] { 0.1f, 0.2f, 0.3f };
        var tensor = EcapaSpeakerEmbedder.WaveformTensor(samples, [1, -1]);

        Assert.Equal([1, 3], tensor.Dimensions.ToArray());
        Assert.Equal(0.2f, tensor[0, 1]);
    }
}
