namespace Verso.Core.Catalogs;

/// <summary>
/// Cor padrão por nome de tag, replicando exatamente o dicionário TAG_COLORS do protótipo
/// (transcriba-v2-icons-transcriptions.html), com fallback "blue" para tags novas/desconhecidas.
/// </summary>
public static class TagColorCatalog
{
    private static readonly IReadOnlyDictionary<string, string> TagColors = new Dictionary<string, string>
    {
        ["mobilidade"] = "blue",
        ["educação"] = "green",
        ["saúde"] = "red",
        ["políticas públicas"] = "purple",
        ["entrevista"] = "orange",
        ["campo"] = "yellow",
        ["tese"] = "pink",
        ["acessibilidade"] = "green"
    };

    public static string GetColor(string tagName) =>
        TagColors.TryGetValue(tagName, out var color) ? color : "blue";
}
