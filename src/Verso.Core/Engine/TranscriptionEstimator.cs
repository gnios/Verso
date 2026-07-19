using System.Collections.Concurrent;
using Verso.Core.Data.Entities;

namespace Verso.Core.Engine;

/// <summary>
/// Estimativa de tempo de transcrição. O sistema aprende com transcrições anteriores:
/// a cada job concluído, o RTF real é armazenado e usado como referência principal.
/// Os valores estáticos servem apenas como fallback inicial (antes da primeira transcrição).
/// </summary>
public static class TranscriptionEstimator
{
    private static readonly ConcurrentDictionary<(ModelQuality Quality, ExecutionDevice Device), double> _rtfCache = new();
    private static readonly ConcurrentDictionary<(ModelQuality Quality, ExecutionDevice Device), int> _rtfCount = new();

    /// <summary>
    /// Registra o RTF real de uma transcrição concluída para refinar estimativas futuras.
    /// Usa média corrida: (atual * count + novo) / (count + 1).
    /// </summary>
    public static void RecordRtf(ModelQuality quality, ExecutionDevice device, double actualRtf)
    {
        if (actualRtf <= 0 || actualRtf > 100) return; // ignora outliers

        var key = (quality, device);
        var currentSum = _rtfCache.GetValueOrDefault(key, 0.0) * _rtfCount.GetValueOrDefault(key, 0);
        var newCount = _rtfCount.AddOrUpdate(key, 1, (_, c) => c + 1);
        var newAvg = (currentSum + actualRtf) / newCount;
        _rtfCache[key] = newAvg;
    }

    /// <summary>True se já existe RTF aprendido para esta combinação modelo+dispositivo.</summary>
    public static bool IsLearned(ModelQuality quality, ExecutionDevice device) =>
        _rtfCache.ContainsKey((quality, device));

    /// <summary>
    /// RTF estimado (1.0 = 1s de áudio leva 1s para transcrever).
    /// Prefere o valor aprendido; usa hardcoded como fallback.
    /// </summary>
    public static double GetRtf(ModelQuality quality, ExecutionDevice device)
    {
        // 1. Valor aprendido (média corrida de transcrições reais)
        if (_rtfCache.TryGetValue((quality, device), out var learnedRtf))
            return learnedRtf;

        // 2. Auto: decide com base no runtime carregado
        var effectiveDevice = device == ExecutionDevice.Auto
            ? (WhisperRuntimeInspector.IsGpuRuntime ? ExecutionDevice.Cuda : ExecutionDevice.Cpu)
            : device;

        // 3. Fallback hardcoded
        return (quality, effectiveDevice) switch
        {
            // CPU
            (ModelQuality.Tiny, ExecutionDevice.Cpu) or (ModelQuality.TinyEn, ExecutionDevice.Cpu) => 0.3,
            (ModelQuality.Base, ExecutionDevice.Cpu) or (ModelQuality.BaseEn, ExecutionDevice.Cpu) => 0.5,
            (ModelQuality.Standard, ExecutionDevice.Cpu) or (ModelQuality.SmallEn, ExecutionDevice.Cpu) => 1.0,
            (ModelQuality.Medium, ExecutionDevice.Cpu) or (ModelQuality.MediumEn, ExecutionDevice.Cpu) => 3.0,
            (ModelQuality.High, ExecutionDevice.Cpu) or (ModelQuality.LargeV1, ExecutionDevice.Cpu)
                or (ModelQuality.LargeV2, ExecutionDevice.Cpu) => 10.0,
            (ModelQuality.LargeV3Turbo, ExecutionDevice.Cpu) => 4.0,

            // CUDA
            (ModelQuality.Tiny, ExecutionDevice.Cuda) or (ModelQuality.TinyEn, ExecutionDevice.Cuda) => 0.03,
            (ModelQuality.Base, ExecutionDevice.Cuda) or (ModelQuality.BaseEn, ExecutionDevice.Cuda) => 0.05,
            (ModelQuality.Standard, ExecutionDevice.Cuda) or (ModelQuality.SmallEn, ExecutionDevice.Cuda) => 0.06,
            (ModelQuality.Medium, ExecutionDevice.Cuda) or (ModelQuality.MediumEn, ExecutionDevice.Cuda) => 0.15,
            (ModelQuality.High, ExecutionDevice.Cuda) or (ModelQuality.LargeV1, ExecutionDevice.Cuda)
                or (ModelQuality.LargeV2, ExecutionDevice.Cuda) => 0.50,
            (ModelQuality.LargeV3Turbo, ExecutionDevice.Cuda) => 0.15,

            // Vulkan
            (ModelQuality.Tiny, ExecutionDevice.Vulkan) or (ModelQuality.TinyEn, ExecutionDevice.Vulkan) => 0.05,
            (ModelQuality.Base, ExecutionDevice.Vulkan) or (ModelQuality.BaseEn, ExecutionDevice.Vulkan) => 0.08,
            (ModelQuality.Standard, ExecutionDevice.Vulkan) or (ModelQuality.SmallEn, ExecutionDevice.Vulkan) => 0.10,
            (ModelQuality.Medium, ExecutionDevice.Vulkan) or (ModelQuality.MediumEn, ExecutionDevice.Vulkan) => 0.25,
            (ModelQuality.High, ExecutionDevice.Vulkan) or (ModelQuality.LargeV1, ExecutionDevice.Vulkan)
                or (ModelQuality.LargeV2, ExecutionDevice.Vulkan) => 0.80,
            (ModelQuality.LargeV3Turbo, ExecutionDevice.Vulkan) => 0.25,

            _ => 2.0,
        };
    }

    /// <summary>
    /// Estima o tempo total de transcrição em segundos.
    /// </summary>
    public static double EstimateTotalSeconds(double audioDurationSeconds, ModelQuality quality, ExecutionDevice device = ExecutionDevice.Auto) =>
        audioDurationSeconds * GetRtf(quality, device);
}
