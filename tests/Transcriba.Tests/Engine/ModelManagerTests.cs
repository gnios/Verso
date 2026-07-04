using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;
using Whisper.net.Ggml;

namespace Transcriba.Tests.Engine;

public class ModelManagerTests
{
    [Theory]
    [InlineData(ModelQuality.Standard, GgmlType.Small, "ggml-small.bin")]
    [InlineData(ModelQuality.High, GgmlType.LargeV3, "ggml-large-v3.bin")]
    public void MapQualityToGgmlType_MapsExpectedValues(ModelQuality quality, GgmlType expectedType, string expectedFileName)
    {
        Assert.Equal(expectedType, ModelManager.MapQualityToGgmlType(quality));
        Assert.Equal(expectedFileName, ModelManager.GetModelFileName(quality));
    }
}
