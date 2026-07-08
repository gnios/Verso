namespace Verso.Core.Catalogs;

/// <summary>
/// Paleta de cores de locutores, replicando o array <c>colors</c> de <c>addNewSpeaker</c>
/// do protótipo (transcriba-v2-icons-transcriptions.html).
/// </summary>
public static class SpeakerColorCatalog
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#2eaadc",
        "#9065b0",
        "#4dab6f",
        "#d9730d",
        "#cb912f",
        "#eb5757",
        "#c14c8a",
        "#0d9488"
    ];

    public static string ColorAtIndex(int index) => Colors[index % Colors.Count];
}
