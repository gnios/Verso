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

        if (device == ExecutionDevice.Vulkan && quality.HasValue && OperatingSystem.IsWindows())
        {
            var requiredBytes = EstimateVulkanVramBytes(quality.Value);
            var availableBytes = VulkanDeviceEnumerator.TryGetDedicatedGpuVramBytes();

            if (availableBytes > 0 && availableBytes < requiredBytes)
            {
                // VRAM insuficiente: força CPU antes de Vulkan na ordem de runtime.
                // whisper.net carrega a primeira lib disponível — com CPU primeiro,
                // Vulkan nunca será tentado, evitando o OOM no meio da transcrição.
                VramFallbackReason =
                    $"VRAM insuficiente detectada: GPU dedicada tem {availableBytes / (1024.0 * 1024.0 * 1024.0):F1} GB, " +
                    $"modelo {quality.Value} requer ~{requiredBytes / (1024.0 * 1024.0 * 1024.0):F1} GB. " +
                    "Fallback automático para CPU.";
                order =
                [
                    RuntimeLibrary.Cpu,
                    RuntimeLibrary.CpuNoAvx,
                ];
            }
        }

        RuntimeOptions.RuntimeLibraryOrder = order;

        CurrentGpuDevice = device switch
        {
            ExecutionDevice.Vulkan when OperatingSystem.IsWindows() && VramFallbackReason is null => ResolveVulkanDeviceIndex(),
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
        ModelQuality.PtBrTurbo => 1_500_000_000,                         // ~1.5 GB (Q5_0, compacto)
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
