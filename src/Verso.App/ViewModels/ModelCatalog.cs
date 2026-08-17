using System.Collections.Generic;
using System.Linq;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;

namespace Verso.App.ViewModels;

/// <summary>
/// Catálogo de perfis de precisão para a UI (Rápido / Equilibrado / Preciso).
/// Qualidades legadas do enum continuam válidas no engine/DB e são mapeadas
/// para o perfil mais próximo via <see cref="Find"/> / <see cref="ResolveProfile"/>.
/// </summary>
public static class ModelCatalog
{
    public static IReadOnlyList<ModelOptionViewModel> All { get; } =
    [
        new(
            ModelQuality.Base,
            "Rápido",
            "~142 MB",
            "Rascunho e revisão rápida",
            "Mais rápido",
            "Ideal para checar se o áudio está ok ou fazer uma primeira passagem."),
        new(
            ModelQuality.Standard,
            "Equilibrado",
            "~466 MB",
            "Bom para a maioria das entrevistas",
            "Tempo médio",
            "Funciona bem na maioria das entrevistas, aulas e notas de campo."),
        new(
            ModelQuality.LargeV3Turbo,
            "Preciso",
            "~1,2 GB",
            "Citação e análise fina",
            "Mais lento · maior qualidade",
            "Use quando for citar trechos na tese ou o áudio for difícil de entender."),
    ];

    public static IReadOnlyList<EngineOptionViewModel> Engines { get; } =
    [
        new(TranscriptionEngineKind.Parakeet, "Parakeet", "Padrão · rápido em CPU · pt-BR TAGARELA"),
        new(TranscriptionEngineKind.Whisper, "Whisper", "Mais idiomas · mais lento em CPU"),
    ];

    public static IReadOnlyList<ParakeetModelOptionViewModel> ParakeetModels { get; } =
    [
        new(ParakeetModel.PtBrTagarela, "pt-BR TAGARELA", ParakeetModelManager.GetSizeLabel(ParakeetModel.PtBrTagarela)),
        new(ParakeetModel.MultilingualV3, "TDT v3 (pt/en/es)", ParakeetModelManager.GetSizeLabel(ParakeetModel.MultilingualV3)),
    ];

    /// <summary>
    /// Localiza o perfil de UI para uma qualidade persistida (incluindo legados).
    /// Fallback: Equilibrado.
    /// </summary>
    public static ModelOptionViewModel Find(ModelQuality value)
    {
        var profile = ResolveProfile(value);
        foreach (var option in All)
        {
            if (option.Value == profile)
            {
                return option;
            }
        }

        return All[1];
    }

    public static EngineOptionViewModel FindEngine(TranscriptionEngineKind value) =>
        Engines.FirstOrDefault(o => o.Value == value) ?? Engines[0];

    public static ParakeetModelOptionViewModel FindParakeet(ParakeetModel value) =>
        ParakeetModels.FirstOrDefault(o => o.Value == value) ?? ParakeetModels[0];

    /// <summary>
    /// Mapeia qualquer <see cref="ModelQuality"/> (incl. legados) para um dos 3 perfis.
    /// </summary>
    public static ModelQuality ResolveProfile(ModelQuality quality) => quality switch
    {
        ModelQuality.Tiny or ModelQuality.TinyEn
            or ModelQuality.Base or ModelQuality.BaseEn => ModelQuality.Base,

        ModelQuality.Standard or ModelQuality.SmallEn
            or ModelQuality.Medium or ModelQuality.MediumEn => ModelQuality.Standard,

        // Large* / High / desconhecido → Preciso
        _ => ModelQuality.LargeV3Turbo,
    };
}

public sealed record EngineOptionViewModel(TranscriptionEngineKind Value, string Label, string Hint);

public sealed record ParakeetModelOptionViewModel(ParakeetModel Value, string Label, string SizeLabel)
{
    public override string ToString() => $"{Label} · {SizeLabel}";
}
