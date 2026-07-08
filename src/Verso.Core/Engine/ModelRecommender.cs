using System;
using Verso.Core.Data.Entities;

namespace Verso.Core.Engine;

/// <summary>
/// Recomendação dinâmica de modelo Whisper a partir do dispositivo de execução
/// escolhido e da memória física total do computador. Lógica pura e testável —
/// a leitura de RAM (ambiente-dependente) fica em <see cref="SystemMemory"/>.
/// </summary>
public static class ModelRecommender
{
    /// <summary>Modelo recomendado + justificativa curta em pt-BR.</summary>
    public sealed record Recommendation(ModelQuality Quality, string Reason);

    /// <summary>
    /// Sugere um modelo multilíngue adequado. Nunca recomenda variantes "En"
    /// (inglês-only), pois o app é multilíngue por padrão.
    /// </summary>
    /// <param name="device">Dispositivo de execução escolhido pelo usuário (CPU/Cuda/Vulkan/Auto).</param>
    /// <param name="ramBytes">Memória física total em bytes (de <see cref="SystemMemory"/>).</param>
    public static Recommendation Recommend(ExecutionDevice device, long ramBytes)
    {
        var ramGb = ramBytes <= 0 ? 16 : ramBytes / (1024L * 1024L * 1024L);

        return device switch
        {
            ExecutionDevice.Cuda or ExecutionDevice.Vulkan => RecommendGpu(ramGb),
            ExecutionDevice.Cpu => RecommendCpu(ramGb),
            // Auto: não sabemos se há GPU disponível — recomenda de forma conservadora (como CPU).
            _ => RecommendCpu(ramGb),
        };
    }

    private static Recommendation RecommendCpu(long ramGb) =>
        ramGb switch
        {
            < 6 => new(ModelQuality.Tiny,
                "Pouca memória para CPU — Tiny é rápido e leve, ideal para testes rápidos."),
            < 12 => new(ModelQuality.Base,
                "Memória moderada para CPU — Base equilibra velocidade e precisão."),
            < 24 => new(ModelQuality.Standard,
                "Memória adequada para CPU — Padrão (small) oferece boa precisão sem ser lento demais."),
            _ => new(ModelQuality.Medium,
                "Memória ampla para CPU — Medium dá alta precisão, mas a transcrição será mais lenta."),
        };

    private static Recommendation RecommendGpu(long ramGb) =>
        ramGb switch
        {
            < 8 => new(ModelQuality.LargeV3Turbo,
                "GPU ativa com memória limitada — Large-v3-turbo equilibra velocidade e qualidade."),
            < 32 => new(ModelQuality.LargeV3Turbo,
                "GPU ativa — Large-v3-turbo é a melhor relação velocidade/qualidade."),
            _ => new(ModelQuality.High,
                "GPU ativa com memória ampla — Alta (large-v3) entrega a melhor qualidade disponível."),
        };
}