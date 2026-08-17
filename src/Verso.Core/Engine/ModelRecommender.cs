using System;
using Verso.Core.Data.Entities;

namespace Verso.Core.Engine;

/// <summary>
/// Recomendação dinâmica de perfil de precisão a partir do dispositivo e da RAM.
/// Sempre retorna um dos três perfis de UI: Base (Rápido), Standard (Equilibrado),
/// LargeV3Turbo (Preciso).
/// </summary>
public static class ModelRecommender
{
    /// <summary>Modelo recomendado + justificativa curta em pt-BR.</summary>
    public sealed record Recommendation(ModelQuality Quality, string Reason);

    /// <param name="device">Dispositivo de execução escolhido pelo usuário.</param>
    /// <param name="ramBytes">Memória física total em bytes (de <see cref="SystemMemory"/>).</param>
    public static Recommendation Recommend(ExecutionDevice device, long ramBytes)
    {
        var ramGb = ramBytes <= 0 ? 16 : ramBytes / (1024L * 1024L * 1024L);

        return device switch
        {
            ExecutionDevice.Cuda or ExecutionDevice.Vulkan or ExecutionDevice.CoreMl => RecommendGpu(ramGb),
            ExecutionDevice.Cpu => RecommendCpu(ramGb),
            // Auto: conservador como CPU (não sabemos se há GPU).
            _ => RecommendCpu(ramGb),
        };
    }

    private static Recommendation RecommendCpu(long ramGb) =>
        ramGb switch
        {
            < 6 => new(ModelQuality.Base,
                "Pouca memória — Rápido é leve e adequado para testes e rascunhos."),
            < 24 => new(ModelQuality.Standard,
                "Memória adequada — Equilibrado oferece boa precisão na maioria das entrevistas."),
            _ => new(ModelQuality.LargeV3Turbo,
                "Memória ampla — Preciso entrega melhor qualidade para citação e análise."),
        };

    private static Recommendation RecommendGpu(long ramGb) =>
        ramGb switch
        {
            < 8 => new(ModelQuality.LargeV3Turbo,
                "Aceleração gráfica com memória limitada — Preciso equilibra velocidade e qualidade."),
            _ => new(ModelQuality.LargeV3Turbo,
                "Aceleração gráfica disponível — Preciso é a melhor relação velocidade/qualidade."),
        };
}
