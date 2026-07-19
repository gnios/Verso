using System.Buffers.Binary;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace Verso.Tests.Engine;

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

    [Fact]
    public void Configure_VulkanWithTinyModel_VramInsufficient_FallsBackToCpu()
    {
        var dGpu = new VulkanDeviceInfo(1, VulkanDeviceType.DiscreteGpu, "NVIDIA Test GPU");
        VulkanDeviceEnumerator.DevicesOverride = () => [dGpu];
        VulkanDeviceEnumerator.VramBytesOverride = () => 512_000_000;
        try
        {
            WhisperRuntimeConfigurator.Configure(ExecutionDevice.Vulkan, ModelQuality.Tiny);

            Assert.NotNull(WhisperRuntimeConfigurator.VramFallbackReason);
            Assert.Contains("VRAM insuficiente", WhisperRuntimeConfigurator.VramFallbackReason);
            Assert.Contains("Tiny", WhisperRuntimeConfigurator.VramFallbackReason);
            Assert.Equal(RuntimeLibrary.Cpu, RuntimeOptions.RuntimeLibraryOrder[0]);
        }
        finally
        {
            VulkanDeviceEnumerator.DevicesOverride = null;
            VulkanDeviceEnumerator.VramBytesOverride = null;
        }
    }

    [Fact]
    public void Configure_CpuDevice_NoVramCheck()
    {
        VulkanDeviceEnumerator.VramBytesOverride = () => 512_000_000;
        try
        {
            WhisperRuntimeConfigurator.Configure(ExecutionDevice.Cpu, ModelQuality.Tiny);

            Assert.Null(WhisperRuntimeConfigurator.VramFallbackReason);
            Assert.Equal(RuntimeLibrary.Cpu, RuntimeOptions.RuntimeLibraryOrder[0]);
        }
        finally
        {
            VulkanDeviceEnumerator.VramBytesOverride = null;
        }
    }

    [Fact]
    public void Configure_VulkanFallback_RuntimeOrderContainsOnlyCpu()
    {
        var dGpu = new VulkanDeviceInfo(1, VulkanDeviceType.DiscreteGpu, "NVIDIA Test GPU");
        VulkanDeviceEnumerator.DevicesOverride = () => [dGpu];
        VulkanDeviceEnumerator.VramBytesOverride = () => 100_000_000;
        try
        {
            WhisperRuntimeConfigurator.Configure(ExecutionDevice.Vulkan, ModelQuality.LargeV3Turbo);

            Assert.Equal(2, RuntimeOptions.RuntimeLibraryOrder.Count);
            Assert.Contains(RuntimeLibrary.Cpu, RuntimeOptions.RuntimeLibraryOrder);
            Assert.Contains(RuntimeLibrary.CpuNoAvx, RuntimeOptions.RuntimeLibraryOrder);
            Assert.DoesNotContain(RuntimeLibrary.Vulkan, RuntimeOptions.RuntimeLibraryOrder);
        }
        finally
        {
            VulkanDeviceEnumerator.DevicesOverride = null;
            VulkanDeviceEnumerator.VramBytesOverride = null;
        }
    }

    [Fact]
    public void Configure_Vulkan_NoDedicatedGpu_FallsBackToCpu()
    {
        // Simula um notebook onde Vulkan só vê a iGPU (Intel) — a dGPU (MX450)
        // não está registrada no ICD Vulkan (driver incompleto, etc.)
        VulkanDeviceEnumerator.DevicesOverride = () =>
        [
            new VulkanDeviceInfo(0, VulkanDeviceType.IntegratedGpu, "Intel(R) UHD Graphics"),
        ];
        try
        {
            WhisperRuntimeConfigurator.Configure(ExecutionDevice.Vulkan, ModelQuality.Standard);

            Assert.NotNull(WhisperRuntimeConfigurator.VramFallbackReason);
            Assert.Contains("Nenhuma GPU dedicada", WhisperRuntimeConfigurator.VramFallbackReason);
            Assert.Equal(RuntimeLibrary.Cpu, RuntimeOptions.RuntimeLibraryOrder[0]);
            Assert.DoesNotContain(RuntimeLibrary.Vulkan, RuntimeOptions.RuntimeLibraryOrder);
            Assert.Equal(0, WhisperRuntimeConfigurator.CurrentGpuDevice);
        }
        finally
        {
            VulkanDeviceEnumerator.DevicesOverride = null;
        }
    }
}