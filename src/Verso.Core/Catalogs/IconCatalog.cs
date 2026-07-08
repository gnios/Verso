namespace Verso.Core.Catalogs;

/// <summary>
/// Ícones disponíveis para pesquisas/teses e para transcrições, replicando exatamente os arrays
/// PAGE_ICONS e TRANS_ICONS do protótipo (transcriba-v2-icons-transcriptions.html).
/// </summary>
public static class IconCatalog
{
    public static readonly IReadOnlyList<string> PageIcons =
    [
        "📚", "🔬", "📝", "🎓", "💡", "🏛️", "📊", "🧠", "🌍", "💊",
        "⚖️", "🎤", "🎥", "🗂️", "💬", "🤝", "🔍", "📐", "📱", "🧬",
        "🎭", "📰", "🎵", "📑", "🧪", "🏫", "📈", "💭", "🎯", "📖"
    ];

    public static readonly IReadOnlyList<string> TransIcons =
    [
        "🎤", "🎙️", "📝", "📖", "📋", "🗣️", "🔊", "🎧", "💡", "🔬",
        "💬", "🎥", "📊", "🗂️", "📑", "✍️", "📞", "🔍"
    ];
}
