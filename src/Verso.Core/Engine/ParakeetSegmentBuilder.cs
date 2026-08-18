namespace Verso.Core.Engine;

/// <summary>
/// Agrupa tokens timestampados em trechos para o editor.
/// O decoder TDT avança até ~8 frames (0,64 s) entre tokens mesmo em fala contínua;
/// um limiar de 0,6 s cortava no meio da frase. Corta só em pausa real (≥ 1,5 s)
/// e limita a duração de um trecho para o editor não virar um bloco só.
/// </summary>
public static class ParakeetSegmentBuilder
{
    public const double DefaultPauseSeconds = 1.5;
    public const double MaxSegmentSeconds = 30;
    public const double TokenEndPaddingSeconds = 0.08;

    public static IReadOnlyList<TranscriptionSegmentResult> Build(
        string fullText,
        IReadOnlyList<double> tokenTimestamps,
        IReadOnlyList<string> tokens,
        double pauseSeconds = DefaultPauseSeconds)
    {
        if (tokens.Count == 0 || string.IsNullOrWhiteSpace(fullText))
        {
            return [];
        }

        if (tokenTimestamps.Count != tokens.Count)
        {
            var end = tokenTimestamps.Count > 0
                ? tokenTimestamps[^1] + TokenEndPaddingSeconds
                : 0;
            return [new TranscriptionSegmentResult(0, Math.Max(end, TokenEndPaddingSeconds), fullText)];
        }

        var segments = new List<TranscriptionSegmentResult>();
        var start = 0;
        for (var i = 1; i <= tokens.Count; i++)
        {
            var isLast = i == tokens.Count;
            var gap = !isLast && tokenTimestamps[i] - tokenTimestamps[i - 1] >= pauseSeconds;
            var tooLong = !isLast
                && tokenTimestamps[i - 1] - tokenTimestamps[start] >= MaxSegmentSeconds;
            if (!isLast && !gap && !tooLong)
            {
                continue;
            }

            var piece = string.Concat(tokens.Skip(start).Take(i - start)).Trim();
            if (piece.Length > 0)
            {
                var segStart = tokenTimestamps[start];
                var segEnd = tokenTimestamps[i - 1] + TokenEndPaddingSeconds;
                if (segEnd <= segStart)
                {
                    segEnd = segStart + TokenEndPaddingSeconds;
                }

                segments.Add(new TranscriptionSegmentResult(segStart, segEnd, piece));
            }

            start = i;
        }

        return segments;
    }
}
