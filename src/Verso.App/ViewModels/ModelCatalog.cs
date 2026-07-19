using System.Collections.Generic;
using Verso.Core.Data.Entities;

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
            "Rascunho e revisão rápida"),
        new(
            ModelQuality.Standard,
            "Equilibrado",
            "~466 MB",
            "Recomendado para a maioria das entrevistas"),
        new(
            ModelQuality.LargeV3Turbo,
            "Preciso",
            "~1,2 GB",
            "Citação e análise fina"),
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
