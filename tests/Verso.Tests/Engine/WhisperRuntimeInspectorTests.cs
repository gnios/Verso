using Verso.Core.Engine;
using Verso.Core.Data.Entities;
using Whisper.net.LibraryLoader;

namespace Verso.Tests.Engine;

public class WhisperRuntimeInspectorTests
{
    [Theory]
    [InlineData(RuntimeLibrary.Cpu, "CPU")]
    [InlineData(RuntimeLibrary.CpuNoAvx, "CPU (sem AVX)")]
    [InlineData(RuntimeLibrary.Cuda, "CUDA")]
    [InlineData(RuntimeLibrary.Cuda12, "CUDA 12")]
    [InlineData(RuntimeLibrary.Vulkan, "Vulkan")]
    [InlineData(RuntimeLibrary.CoreML, "CoreML")]
    [InlineData(RuntimeLibrary.OpenVino, "OpenVINO")]
    public void GetRuntimeLabel_ReturnsPtBrLabel(RuntimeLibrary lib, string expected)
    {
        Assert.Equal(expected, WhisperRuntimeInspector.GetRuntimeLabel(lib));
    }

    [Theory]
    [InlineData(RuntimeLibrary.Cpu, "CPU")]
    [InlineData(RuntimeLibrary.CpuNoAvx, "CPU")]
    [InlineData(RuntimeLibrary.Cuda, "CUDA")]
    [InlineData(RuntimeLibrary.Cuda12, "CUDA")]
    [InlineData(RuntimeLibrary.Vulkan, "Vulkan")]
    public void GetBackend_CategorizesToCpuCudaVulkan(RuntimeLibrary lib, string expected)
    {
        Assert.Equal(expected, WhisperRuntimeInspector.GetBackend(lib));
    }

    [Fact]
    public void GetBackend_Null_ReturnsNull()
    {
        Assert.Null(WhisperRuntimeInspector.GetBackend(null));
    }

    [Theory]
    [InlineData(RuntimeLibrary.Cuda, true)]
    [InlineData(RuntimeLibrary.Cuda12, true)]
    [InlineData(RuntimeLibrary.Vulkan, true)]
    [InlineData(RuntimeLibrary.Cpu, false)]
    [InlineData(RuntimeLibrary.CpuNoAvx, false)]
    public void IsGpuRuntimeReflectsCurrentLoadedRuntime(RuntimeLibrary loaded, bool expectGpu)
    {
        // IsGpuRuntime lê o estado estático LoadedLibrary do whisper.net. Em um teste
        // isolado, só conseguemos exercer a lógica de mapeamento (não o estado real),
        // então validamos o predicado sobre cada valor possível via GetBackend.
        var isGpu = WhisperRuntimeInspector.GetBackend(loaded) is "CUDA" or "Vulkan";
        Assert.Equal(expectGpu, isGpu);
    }

    [Fact]
    public void DescribeRuntimeOrder_JoinsLabelsForDevice()
    {
        var desc = WhisperRuntimeInspector.DescribeRuntimeOrder(ExecutionDevice.Cuda);
        Assert.Contains("CUDA", desc);
        Assert.Contains("CPU", desc);
        Assert.Contains("→", desc);
    }

    [Fact]
    public void ProbeRuntime_NonExistentModel_ReturnsNull()
    {
        // Sem arquivo de modelo, o probe não tenta carregar a lib nativa — devolve null
        // sem lançar (guarda de File.Exists antes da carga).
        var result = WhisperRuntimeInspector.ProbeRuntime(
            Path.Combine(Path.GetTempPath(), $"verso-no-model-{Guid.NewGuid():N}.bin"),
            ExecutionDevice.Cpu);
        Assert.Null(result);
    }
}