using Verso.App.Services;

namespace Verso.Tests.Services;

public class ActiveGpuResolverTests
{
    [Fact]
    public void Resolve_Cpu_ReturnsCpuBackend()
    {
        var resolver = new ActiveGpuResolver(new GpuDetector());
        var info = resolver.Resolve("CPU");

        Assert.Equal("CPU", info.GpuName);
        Assert.Equal("Processador", info.Source);
        Assert.Contains("CPU", info.Note);
        Assert.Null(info.WmiSource);
    }

    [Fact]
    public void Resolve_Null_ReturnsUnknown()
    {
        var resolver = new ActiveGpuResolver(new GpuDetector());
        var info = resolver.Resolve(null);

        Assert.Null(info.GpuName);
        Assert.Equal("Desconhecido", info.Source);
        Assert.Contains("carregado", info.Note);
    }

    [Fact]
    public void Resolve_Cuda_DoesNotThrowAndMentionsDevice0OrDriver()
    {
        // nvidia-smi pode estar ausente no runner — qualquer caminho (device 0 via
        // nvidia-smi, fallback WMI dedicada, ou "CUDA sem GPU") deve produzir uma
        // nota não-vazia sem lançar.
        var resolver = new ActiveGpuResolver(new GpuDetector());
        var info = resolver.Resolve("CUDA");

        Assert.False(string.IsNullOrEmpty(info.Note));
        // Source reflete como determinamos: nvidia-smi, WMI, ou "Indisponível".
        Assert.True(info.Source is "nvidia-smi (CUDA device 0)"
            or "WMI (nvidia-smi ausente)"
            or "Indisponível");
    }

    [Fact]
    public void Resolve_Vulkan_DoesNotThrowAndProducesNote()
    {
        var resolver = new ActiveGpuResolver(new GpuDetector());
        var info = resolver.Resolve("Vulkan");

        Assert.False(string.IsNullOrEmpty(info.Note));
    }

    [Fact]
    public void QueryNvidiaSmi_OnNonNvidiaMachine_ReturnsEmptyWithoutThrowing()
    {
        var resolver = new ActiveGpuResolver(new GpuDetector());
        var gpus = resolver.QueryNvidiaSmi();

        // Não lanstra; pode ser vazia (sem driver NVIDIA) ou conter GPUs NVIDIA.
        Assert.NotNull(gpus);
    }
}