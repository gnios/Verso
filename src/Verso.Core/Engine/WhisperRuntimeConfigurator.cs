using Verso.Core.Data.Entities;
using Whisper.net.LibraryLoader;

namespace Verso.Core.Engine;

public static class WhisperRuntimeConfigurator
{
    /// <summary>
    /// Índice do dispositivo GPU para <c>WhisperFactoryOptions.GpuDevice</c>.
    /// Default 0. Quando Vulkan é selecionado no Windows, consultamos
    /// <see cref="VulkanDeviceEnumerator.TryEnumerateDevices"/> para encontrar
    /// o índice correto da GPU dedicada (dGPU) no backend Vulkan — em notebooks
    /// Optimus tipicamente é 1 (iGPU=0, dGPU=1), em desktops com só dGPU é 0.
    /// </summary>
    public static int CurrentGpuDevice { get; set; } = 0;

    public static void Configure(ExecutionDevice device)
    {
        RuntimeOptions.RuntimeLibraryOrder = ResolveRuntimeOrder(device);

        CurrentGpuDevice = device switch
        {
            ExecutionDevice.Vulkan when OperatingSystem.IsWindows() => ResolveVulkanDeviceIndex(),
            _ => 0,
        };
    }

    /// <summary>
    /// Consulta o enumerador Vulkan para obter o índice da GPU dedicada.
    /// Retorna 0 (default) em qualquer falha — o whisper.cpp fará fallback para
    /// CPU se device 0 não existir ou não for compatível.
    /// </summary>
    private static int ResolveVulkanDeviceIndex()
    {
        try
        {
            var devices = VulkanDeviceEnumerator.TryEnumerateDevices();
            if (devices.Count == 0)
                return 0;

            // Prefere GPU dedicada (NVIDIA/AMD dGPU)
            var dedicated = devices.FirstOrDefault(d => d.DeviceType == VulkanDeviceType.DiscreteGpu);
            if (dedicated is not null)
                return dedicated.Index;

            // Sem GPU dedicada: usa o primeiro dispositivo disponível (pode ser iGPU)
            return devices[0].Index;
        }
        catch
        {
            return 0;
        }
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
