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

    /// <summary>
    /// Razão do fallback de Vulkan para CPU, ou null se Vulkan está ativo.
    /// Populada por <see cref="Configure"/> quando <paramref name="quality"/> é informado.
    /// </summary>
    public static string? VramFallbackReason { get; private set; }

    public static void Configure(ExecutionDevice device, ModelQuality? quality = null)
    {
        VramFallbackReason = null;
        var order = ResolveRuntimeOrder(device);
        int? vulkanDeviceIndex = null;

        if (device == ExecutionDevice.Vulkan && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
        {
            // Verifica se existe GPU dedicada visível ao Vulkan.
            // Em notebooks com Optimus, a dGPU pode não estar registrada
            // no ICD Vulkan (driver incompleto, etc.) — nesse caso,
            // forçamos CPU para evitar rodar na iGPU silenciosamente.
            vulkanDeviceIndex = ResolveVulkanDeviceIndex();
            if (vulkanDeviceIndex is null)
            {
                VramFallbackReason =
                    "Nenhuma GPU dedicada (DiscreteGpu) encontrada via Vulkan. " +
                    "Verifique se o driver da GPU e o Vulkan Runtime estão instalados. " +
                    "Fallback automático para CPU.";
                order = [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
            }
            else if (quality.HasValue)
            {
                // GPU dedicada encontrada — verifica se tem VRAM suficiente
                var requiredBytes = EstimateVulkanVramBytes(quality.Value);
                var availableBytes = VulkanDeviceEnumerator.TryGetDedicatedGpuVramBytes();

                if (availableBytes > 0 && availableBytes < requiredBytes)
                {
                    VramFallbackReason =
                        $"VRAM insuficiente detectada: GPU dedicada tem {availableBytes / (1024.0 * 1024.0 * 1024.0):F1} GB, " +
                        $"modelo {quality.Value} requer ~{requiredBytes / (1024.0 * 1024.0 * 1024.0):F1} GB. " +
                        "Fallback automático para CPU.";
                    order = [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
                }
            }
        }

        RuntimeOptions.RuntimeLibraryOrder = order;
        CurrentGpuDevice = vulkanDeviceIndex ?? 0;
    }

    /// <summary>
    /// Consulta o enumerador Vulkan para obter o índice da GPU dedicada.
    /// Retorna <c>null</c> quando nenhuma GPU discreta (DiscreteGpu) é encontrada —
    /// o chamador deve fazer fallback para CPU nesse caso.
    /// Retorna 0 (iGPU) apenas em caso de falha inesperada (exceção).
    /// </summary>
    private static int? ResolveVulkanDeviceIndex()
    {
        try
        {
            var devices = VulkanDeviceEnumerator.TryEnumerateDevices();
            if (devices.Count == 0)
                return null;

            // Prefere GPU dedicada (NVIDIA/AMD dGPU)
            var dedicated = devices.FirstOrDefault(d => d.DeviceType == VulkanDeviceType.DiscreteGpu);
            if (dedicated is not null)
                return dedicated.Index;

            // Nenhuma GPU dedicada encontrada via Vulkan.
            // Em notebooks com Optimus, a dGPU pode não estar registrada
            // no ICD Vulkan se o driver estiver incompleto.
            return null;
        }
        catch
        {
            return 0; // erro inesperado → fallback seguro
        }
    }

    /// <summary>
    /// Estima a VRAM necessária para rodar o modelo com backend Vulkan.
    /// Inclui o modelo carregado (~tamanho do arquivo GGML) + overhead de buffers
    /// de staging/compute do Vulkan (~500 MB para modelos small+).
    /// Valores baseados nos números de memória do whisper.cpp (CPU) + 500 MB Vulkan.
    /// </summary>
    private static long EstimateVulkanVramBytes(ModelQuality quality) => quality switch
    {
        ModelQuality.Tiny or ModelQuality.TinyEn => 1_000_000_000,       // ~1 GB
        ModelQuality.Base or ModelQuality.BaseEn => 1_000_000_000,       // ~1 GB
        ModelQuality.Standard => 1_500_000_000,                          // ~1.5 GB (Small)
        ModelQuality.SmallEn => 1_500_000_000,                           // ~1.5 GB
        ModelQuality.Medium or ModelQuality.MediumEn => 3_000_000_000,   // ~3 GB
        ModelQuality.High or ModelQuality.LargeV1 or ModelQuality.LargeV2
            or ModelQuality.LargeV3Turbo => 4_500_000_000,                // ~4.5 GB
        _ => 4_500_000_000,                                              // conservador
    };

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
            ExecutionDevice.CoreMl =>
            [
                RuntimeLibrary.CoreML,
                RuntimeLibrary.Cpu,
                RuntimeLibrary.CpuNoAvx,
            ],
            ExecutionDevice.Auto when OperatingSystem.IsMacOS() =>
            [
                RuntimeLibrary.CoreML,
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
