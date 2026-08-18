using System.Globalization;
using System.Text;

namespace Verso.Core.Export;

/// <summary>
/// Formatação de saída TXT alinhada a <c>transcrever.cs</c> (FormatarSegmento / FormatarTempo).
/// </summary>
public static class TranscriptionTextFormatter
{
    public static string FormatSegment(double startSeconds, double endSeconds, string text)
    {
        return $"[{startSeconds,6:F2}s -> {endSeconds,6:F2}s] {text.Trim()}";
    }

    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}m {duration.Seconds + duration.Milliseconds / 1000.0:F2}s"
            : $"{duration.TotalSeconds:F2}s";

    public static IReadOnlyList<string> BuildTxtHeader(
        string title,
        string? mediaFileName,
        string language,
        string qualityLabel,
        double durationSeconds)
    {
        return
        [
            $"Título: {title}",
            $"Arquivo: {mediaFileName ?? title}",
            $"Idioma: {language}",
            $"Precisão: {qualityLabel}",
            $"Duração: {FormatDuration(TimeSpan.FromSeconds(durationSeconds))}"
        ];
    }

    public static string FormatSrtTimestamp(double totalSeconds)
    {
        var time = TimeSpan.FromSeconds(totalSeconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";
    }

    public static string FormatVttTimestamp(double totalSeconds)
    {
        var time = TimeSpan.FromSeconds(totalSeconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
    }

    public static string FormatTxtTimestamp(double totalSeconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
        }

        return $"{time.Minutes:00}:{time.Seconds:00}";
    }

    public static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "transcricao";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => Array.IndexOf(invalid, c) >= 0 ? '-' : c).ToArray();
        var sanitized = new string(chars).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? "transcricao" : sanitized;
    }

    public static string BuildCueText(string? speakerName, string text)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrEmpty(speakerName))
        {
            return trimmed;
        }

        return $"{speakerName}: {trimmed}";
    }
}
