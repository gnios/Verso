using System.Buffers.Binary;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace Transcriba.Tests.Engine;

public class ModelManagerTests
{
    [Theory]
[InlineData(ModelQuality.Standard, GgmlType.Small, "ggml-small.bin")]
[InlineData(ModelQuality.High, GgmlType.LargeV3, "ggml-large-v3.bin")]
[InlineData(ModelQuality.Tiny, GgmlType.Tiny, "ggml-tiny.bin")]
[InlineData(ModelQuality.Base, GgmlType.Base, "ggml-base.bin")]
[InlineData(ModelQuality.Medium, GgmlType.Medium, "ggml-medium.bin")]
[InlineData(ModelQuality.LargeV2, GgmlType.LargeV2, "ggml-large-v2.bin")]
[InlineData(ModelQuality.LargeV3Turbo, GgmlType.LargeV3Turbo, "ggml-large-v3-turbo.bin")]
[InlineData(ModelQuality.LargeV1, GgmlType.LargeV1, "ggml-large-v1.bin")]
[InlineData(ModelQuality.TinyEn, GgmlType.TinyEn, "ggml-tiny.en.bin")]
[InlineData(ModelQuality.BaseEn, GgmlType.BaseEn, "ggml-base.en.bin")]
[InlineData(ModelQuality.SmallEn, GgmlType.SmallEn, "ggml-small.en.bin")]
[InlineData(ModelQuality.MediumEn, GgmlType.MediumEn, "ggml-medium.en.bin")]
    public void MapQualityToGgmlType_MapsExpectedValues(ModelQuality quality, GgmlType expectedType, string expectedFileName)
    {
        Assert.Equal(expectedType, ModelManager.MapQualityToGgmlType(quality));
        Assert.Equal(expectedFileName, ModelManager.GetModelFileName(quality));
    }

    [Fact]
    public void GetModelFileName_PtBrTurbo_ReturnsCustomGgmlFileName()
    {
        // PtBrTurbo não tem GgmlType canônico — nome de arquivo é fixo (modelo fine-tuned pt-BR).
        Assert.Equal("ggml-distil-large-v3-ptbr-q5_0.bin", ModelManager.GetModelFileName(ModelQuality.PtBrTurbo));
    }

    [Fact]
    public void IsModelFileValid_RejectsPartialDownload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"transcriba-partial-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[965_686]);

        try
        {
            Assert.False(ModelManager.IsModelFileValid(path, GgmlType.Small));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsModelFileValid_AcceptsFileAboveMinimumSizeWithGgmlMagic()
    {
        var path = Path.Combine(Path.GetTempPath(), $"transcriba-valid-{Guid.NewGuid():N}.bin");

        try
        {
            using (var stream = File.Create(path))
            {
                Span<byte> magic = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(magic, 0x67676d6c);
                stream.Write(magic);
                stream.SetLength(ModelManager.GetMinimumModelSizeBytes(GgmlType.Small));
            }

            Assert.True(ModelManager.IsModelFileValid(path, GgmlType.Small));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void IsModelFileValid_AcceptsLittleEndianGgmlMagicFromWhisperNet()
    {
        var path = Path.Combine(Path.GetTempPath(), $"transcriba-hf-magic-{Guid.NewGuid():N}.bin");

        try
        {
            File.WriteAllBytes(path, new byte[] { 0x6c, 0x6d, 0x67, 0x67 });
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write))
            {
                stream.SetLength(ModelManager.GetMinimumModelSizeBytes(GgmlType.Small));
            }

            Assert.True(ModelManager.IsModelFileValid(path, GgmlType.Small));
            Assert.Equal("GGML", ModelManager.DescribeModelMagic(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void IsModelFileValid_RejectsLargeFileWithoutGgmlMagic()
    {
        var path = Path.Combine(Path.GetTempPath(), $"transcriba-no-magic-{Guid.NewGuid():N}.bin");

        try
        {
            using (var stream = File.Create(path))
            {
                stream.SetLength(ModelManager.GetMinimumModelSizeBytes(GgmlType.Small));
            }

            Assert.False(ModelManager.IsModelFileValid(path, GgmlType.Small));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

public class WhisperRuntimeConfiguratorTests
{
    [Fact]
    public void ResolveRuntimeOrder_Auto_IncludesCpuFallback()
    {
        var order = WhisperRuntimeConfigurator.ResolveRuntimeOrder(ExecutionDevice.Auto);

        Assert.Contains(RuntimeLibrary.Cpu, order);
        Assert.Equal(RuntimeLibrary.Cuda, order[0]);
        Assert.Equal(RuntimeLibrary.Cpu, order[^2]);
    }

    [Fact]
    public void ResolveRuntimeOrder_Cuda_IncludesCpuFallback()
    {
        var order = WhisperRuntimeConfigurator.ResolveRuntimeOrder(ExecutionDevice.Cuda);

        Assert.Contains(RuntimeLibrary.Cpu, order);
    }
}