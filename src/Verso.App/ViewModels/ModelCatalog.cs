using System.Collections.Generic;
using System.Linq;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;

namespace Verso.App.ViewModels;

/// <summary>
/// Catálogo estático de todos os modelos Whisper.net disponíveis, ordenado
/// (multilíngues por tamanho crescente, depois as variantes inglês-only).
/// Usado por SettingsViewModel e UploadViewModel para popular os seletores.
/// Os dois valores históricos (Standard/High) mantêm seus rótulos "Padrão"/"Alta"
/// para não quebrar a UX existente, mas agora convivem com os demais modelos.
/// </summary>
public static class ModelCatalog
{
    public static IReadOnlyList<ModelOptionViewModel> All { get; } =
    [
        // Multilíngues (por tamanho crescente; turbo posicionado perto do large por velocidade)
        new(ModelQuality.Tiny, "Tiny", "~75 MB", false),
        new(ModelQuality.Base, "Base", "~142 MB", false),
        new(ModelQuality.Standard, "Padrão (small)", "~466 MB", false),
        new(ModelQuality.Medium, "Medium", "~1,5 GB", false),
        new(ModelQuality.LargeV3Turbo, "Large v3-turbo", "~1,2 GB", false),
        new(ModelQuality.LargeV2, "Large v2", "~3 GB", false),
        new(ModelQuality.High, "Alta (large-v3)", "~3 GB", false),
        new(ModelQuality.LargeV1, "Large v1", "~3 GB", false),
        // Modelo pt-BR fine-tuned (distil) — força idioma pt quando selecionado
        new(ModelQuality.PtBrTurbo, "Pt-BR Turbo (distil)", "~538 MB", false),
        // Variantes inglês-only
        new(ModelQuality.TinyEn, "Tiny (inglês)", "~75 MB", true),
        new(ModelQuality.BaseEn, "Base (inglês)", "~142 MB", true),
        new(ModelQuality.SmallEn, "Small (inglês)", "~466 MB", true),
        new(ModelQuality.MediumEn, "Medium (inglês)", "~1,5 GB", true),
    ];

    public static IReadOnlyList<EngineOptionViewModel> Engines { get; } =
    [
        new(TranscriptionEngineKind.Whisper, "Whisper", "Melhor cobertura de idiomas; mais lento em CPU"),
        new(TranscriptionEngineKind.Parakeet, "Parakeet (leve, CPU)", "Rápido sem GPU; pt/en/es"),
    ];

    public static IReadOnlyList<ParakeetModelOptionViewModel> ParakeetModels { get; } =
    [
        new(ParakeetModel.PtBrTagarela, "pt-BR TAGARELA", ParakeetModelManager.GetSizeLabel(ParakeetModel.PtBrTagarela)),
        new(ParakeetModel.MultilingualV3, "TDT v3 (pt/en/es)", ParakeetModelManager.GetSizeLabel(ParakeetModel.MultilingualV3)),
    ];

    /// <summary>Localiza a opção pelo valor do enum, ou a primeira se não achar.</summary>
    public static ModelOptionViewModel Find(ModelQuality value) =>
        _lookup.TryGetValue(value, out var opt) ? opt : All[0];

    public static EngineOptionViewModel FindEngine(TranscriptionEngineKind value) =>
        Engines.FirstOrDefault(o => o.Value == value) ?? Engines[0];

    public static ParakeetModelOptionViewModel FindParakeet(ParakeetModel value) =>
        ParakeetModels.FirstOrDefault(o => o.Value == value) ?? ParakeetModels[0];

    private static readonly Dictionary<ModelQuality, ModelOptionViewModel> _lookup =
        BuildLookup();

    private static Dictionary<ModelQuality, ModelOptionViewModel> BuildLookup()
    {
        var dict = new Dictionary<ModelQuality, ModelOptionViewModel>();
        foreach (var option in All)
        {
            dict[option.Value] = option;
        }

        return dict;
    }
}

public sealed record EngineOptionViewModel(TranscriptionEngineKind Value, string Label, string Hint);

public sealed record ParakeetModelOptionViewModel(ParakeetModel Value, string Label, string SizeLabel)
{
    public override string ToString() => $"{Label} · {SizeLabel}";
}
