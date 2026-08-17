namespace Verso.Core.Engine;

/// <summary>
/// Agrupa tokens timestampados em segmentos para o editor (pausa ≥ 0,6 s).
/// </summary>
public static class ParakeetSegmentBuilder
{
    public const double DefaultPauseSeconds = 0.6;

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
            var end = tokenTimestamps.Count > 0 ? tokenTimestamps[^1] + 0.08 : 0;
            return [new TranscriptionSegmentResult(0, Math.Max(end, 0.08), fullText)];
        }

        var segments = new List<TranscriptionSegmentResult>();
        var start = 0;
        for (var i = 1; i <= tokens.Count; i++)
        {
            var isLast = i == tokens.Count;
            var gap = !isLast && tokenTimestamps[i] - tokenTimestamps[i - 1] >= pauseSeconds;
            if (!isLast && !gap)
            {
                continue;
            }

            var piece = string.Concat(tokens.Skip(start).Take(i - start)).Trim();
            if (piece.Length > 0)
            {
                var segStart = tokenTimestamps[start];
                var segEnd = tokenTimestamps[i - 1] + 0.08;
                if (segEnd <= segStart)
                {
                    segEnd = segStart + 0.08;
                }

                segments.Add(new TranscriptionSegmentResult(segStart, segEnd, piece));
            }

            start = i;
        }

        return segments;
    }
}
