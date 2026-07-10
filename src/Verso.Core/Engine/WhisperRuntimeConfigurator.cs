using Verso.Core.Data.Entities;
using Whisper.net.LibraryLoader;

namespace Verso.Core.Engine;

public static class WhisperRuntimeConfigurator
{
    /// <summary>
    /// Índice do dispositivo GPU para <c>WhisperFactoryOptions.GpuDevice</c>.
    /// Default 0. Para Vulkan em notebooks híbridos (iGPU+dGPU), usa índice 1
    /// que é a GPU dedicada na maioria dos sistemas Optimus.
    /// </summary>
    public static int CurrentGpuDevice { get; set; } = 0;

    public static void Configure(ExecutionDevice device)
    {
        RuntimeOptions.RuntimeLibraryOrder = ResolveRuntimeOrder(device);

        CurrentGpuDevice = device switch
        {
            ExecutionDevice.Vulkan when OperatingSystem.IsWindows() => 1,
            _ => 0,
        };
    }

    public static List<RuntimeLibrary> ResolveRuntimeOrder(ExecutionDevice device) =>
        device switch
        {
            ExecutionDevice.Cpu => [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx],
            ExecutionDevice.Cuda =>
            [
                RuntimeLibrary.Cuda,
                RuntimeLibrary.Cuda12,
                RuntimeLibrary.Cpu,
                RuntimeLibrary.CpuNoAvx,
            ],
            ExecutionDevice.Vulkan =>
            [
                RuntimeLibrary.Vulkan,
                RuntimeLibrary.Cpu,
                RuntimeLibrary.CpuNoAvx,
            ],
            ExecutionDevice.Auto =>
            [
                RuntimeLibrary.Cuda,
                RuntimeLibrary.Cuda12,
                RuntimeLibrary.Vulkan,
                RuntimeLibrary.Cpu,
                RuntimeLibrary.CpuNoAvx,
            ],
            _ => [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx],
        };
}
