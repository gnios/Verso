using Transcriba.Core.Data.Entities;
using Whisper.net.LibraryLoader;

namespace Transcriba.Core.Engine;

public static class WhisperRuntimeConfigurator
{
    public static void Configure(ExecutionDevice device) =>
        RuntimeOptions.RuntimeLibraryOrder = ResolveRuntimeOrder(device);

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
