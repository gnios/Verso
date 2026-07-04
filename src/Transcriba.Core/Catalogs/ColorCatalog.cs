namespace Transcriba.Core.Catalogs;

public readonly record struct PageColor(string Name, string Hex);

/// <summary>
/// Cores disponíveis para pesquisas/teses, replicando exatamente o array PAGE_COLORS do protótipo
/// (transcriba-v2-icons-transcriptions.html).
/// </summary>
public static class ColorCatalog
{
    public static readonly IReadOnlyList<PageColor> PageColors =
    [
        new("blue", "#2eaadc"),
        new("green", "#4dab6f"),
        new("purple", "#9065b0"),
        new("orange", "#d9730d"),
        new("pink", "#c14c8a"),
        new("yellow", "#cb912f"),
        new("red", "#eb5757"),
        new("teal", "#0d9488")
    ];
}
