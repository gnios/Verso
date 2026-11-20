namespace Verso.Core.Engine;

/// <summary>
/// Parte o áudio 16 kHz em janelas curtas para o encoder FastConformer.
/// Uma passada única em dezenas de minutos deixa a atenção O(T²) inviável em CPU
/// (o encoder sozinho chegou a ~7 min num clipe de ~6 min antes de falhar no decoder).
/// 20 s alinha com a janela local típica do Parakeet (~256 frames × 80 ms).
/// </summary>
public static class ParakeetAudioChunker
{
    public const int WindowSeconds = 20;
    public const int OverlapSeconds = 2;

    public readonly record struct Chunk(int OffsetSamples, float[] Samples);

    public static int WindowSamples(int sampleRate = AudioLoader.SampleRate) =>
        WindowSeconds * sampleRate;

    public static int OverlapSamples(int sampleRate = AudioLoader.SampleRate) =>
        OverlapSeconds * sampleRate;

    public static int CountWindows(int sampleCount, int sampleRate = AudioLoader.SampleRate)
    {
        if (sampleCount <= 0)
        {
            return 0;
        }

        var window = WindowSamples(sampleRate);
        if (sampleCount <= window)
        {
            return 1;
        }

        var step = Math.Max(1, window - OverlapSamples(sampleRate));
        return 1 + (int)Math.Ceiling((sampleCount - window) / (double)step);
    }

    public static int CountWindowsFromDuration(double durationSeconds)
    {
        if (durationSeconds <= 0)
        {
            return 1;
        }

        if (durationSeconds <= WindowSeconds)
        {
            return 1;
        }

        var step = WindowSeconds - OverlapSeconds;
        return 1 + (int)Math.Ceiling((durationSeconds - WindowSeconds) / step);
    }

    public static IReadOnlyList<(double StartSeconds, double LengthSeconds)> WindowsFromDuration(
        double durationSeconds)
    {
        if (durationSeconds <= 0)
        {
            return [(0, 0)];
        }

        if (durationSeconds <= WindowSeconds)
        {
            return [(0, durationSeconds)];
        }

        var step = WindowSeconds - OverlapSeconds;
        var windows = new List<(double, double)>();
        for (var start = 0.0; start < durationSeconds; start += step)
        {
            var length = Math.Min(WindowSeconds, durationSeconds - start);
            windows.Add((start, length));
            if (start + length >= durationSeconds)
            {
                break;
            }
        }

        return windows;
    }

    public static IReadOnlyList<Chunk> Split(float[] samples, int sampleRate = AudioLoader.SampleRate)
    {
        if (samples.Length == 0)
        {
            return [];
        }

        var window = WindowSamples(sampleRate);
        var overlap = OverlapSamples(sampleRate);
        if (samples.Length <= window)
        {
            return [new Chunk(0, samples)];
        }

        var step = Math.Max(1, window - overlap);
        var chunks = new List<Chunk>();
        for (var offset = 0; offset < samples.Length; offset += step)
        {
            var length = Math.Min(window, samples.Length - offset);
            var windowSamples = new float[length];
            Array.Copy(samples, offset, windowSamples, 0, length);
            chunks.Add(new Chunk(offset, windowSamples));
            if (offset + length >= samples.Length)
            {
                break;
            }
        }

        return chunks;
    }

    public readonly record struct TimedToken(double TimeSeconds, string Text);

    public readonly record struct WindowTranscript(
        double StartSeconds,
        double LengthSeconds,
        IReadOnlyList<double> Timestamps,
        IReadOnlyList<string> Tokens);

    /// <summary>
    /// Mantém só os tokens da região confiável da janela: depois de overlap/2 no
    /// começo (exceto a primeira) e antes de overlap/2 no fim (exceto a última).
    /// A janela é escolhida pelo primeiro token da palavra, preservando suas continuações.
    /// </summary>
    public static IReadOnlyList<TimedToken> TrimOwnedRegion(
        IReadOnlyList<double> timestamps,
        IReadOnlyList<string> tokens,
        double chunkStartSeconds,
        bool isFirstChunk,
        bool isLastChunk,
        double windowLengthSeconds,
        double overlapSeconds = OverlapSeconds)
    {
        if (timestamps.Count == 0 || tokens.Count == 0)
        {
            return [];
        }

        var keepAfter = isFirstChunk ? 0 : overlapSeconds / 2.0;
        var keepUntil = isLastChunk
            ? double.PositiveInfinity
            : windowLengthSeconds - overlapSeconds / 2.0;
        if (keepUntil < keepAfter)
        {
            keepUntil = keepAfter;
        }

        var window = new WindowTranscript(chunkStartSeconds, windowLengthSeconds, timestamps, tokens);
        var start = FindCut(window, keepAfter);
        var end = FindCut(window, keepUntil);
        return Enumerable.Range(start, end - start)
            .Select(i => new TimedToken(timestamps[i] + chunkStartSeconds, tokens[i]))
            .ToArray();
    }

    /// <summary>
    /// Concatena os tokens confiáveis de todas as janelas e só então agrupa em
    /// trechos. Cortar por janela forçava uma quebra a cada ~18 s, no meio da frase.
    /// </summary>
    public static IReadOnlyList<TranscriptionSegmentResult> StitchWindowTokens(
        IReadOnlyList<WindowTranscript> windows)
    {
        if (windows.Count == 0)
        {
            return [];
        }

        var all = new List<TimedToken>();
        var starts = new int[windows.Count];
        var ends = new int[windows.Count];
        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            var count = Math.Min(window.Tokens.Count, window.Timestamps.Count);
            starts[i] = i == 0 ? 0 : FindCut(window, OverlapSeconds / 2.0);
            ends[i] = i == windows.Count - 1 ? count : FindCut(window, window.LengthSeconds - OverlapSeconds / 2.0);
        }

        for (var i = 1; i < windows.Count; i++)
        {
            var previous = windows[i - 1];
            var current = windows[i];
            var overlapEnd = previous.StartSeconds + previous.LengthSeconds;
            var rightWords = OverlapWords(current, current.StartSeconds, overlapEnd).ToLookup(word => word.Text);
            var leftWords = OverlapWords(previous, current.StartSeconds, overlapEnd).ToLookup(word => word.Text);
            foreach (var group in leftWords)
            {
                var matching = rightWords[group.Key];
                if (group.Count() != 1 || matching.Count() != 1)
                {
                    continue;
                }

                var left = group.Single();
                var right = matching.Single();
                if (Math.Abs(left.Time - right.Time) > 0.4)
                {
                    continue;
                }

                // Corrige a emenda apenas quando a mesma palavra seria repetida ou omitida.
                if (left.End == ends[i - 1] && right.Start == starts[i])
                {
                    starts[i] = right.End;
                    break;
                }

                if (left.Start == ends[i - 1] && right.End == starts[i])
                {
                    ends[i - 1] = left.End;
                    break;
                }
            }
        }

        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            for (var token = starts[i]; token < ends[i]; token++)
            {
                all.Add(new TimedToken(window.StartSeconds + window.Timestamps[token], window.Tokens[token]));
            }
        }

        if (all.Count == 0)
        {
            return [];
        }

        var times = new double[all.Count];
        var toks = new string[all.Count];
        for (var t = 0; t < all.Count; t++)
        {
            times[t] = all[t].TimeSeconds;
            toks[t] = all[t].Text;
        }

        return ParakeetSegmentBuilder.Build(string.Concat(toks), times, toks);
    }

    private static int FindCut(WindowTranscript window, double time)
    {
        var count = Math.Min(window.Tokens.Count, window.Timestamps.Count);
        for (var i = 0; i < count; i++)
        {
            if (window.Timestamps[i] >= time && IsWordStart(window, i))
            {
                return i;
            }
        }

        return count;
    }

    private static IEnumerable<(string Text, int Start, int End, double Time)> OverlapWords(
        WindowTranscript window, double overlapStart, double overlapEnd)
    {
        var count = Math.Min(window.Tokens.Count, window.Timestamps.Count);
        var start = FindCut(window, overlapStart - window.StartSeconds);
        for (var i = start + 1; i <= count; i++)
        {
            if (i < count && !IsWordStart(window, i))
            {
                continue;
            }

            var time = window.StartSeconds + window.Timestamps[start];
            if (time >= overlapEnd)
            {
                yield break;
            }

            var text = string.Concat(window.Tokens.Skip(start).Take(i - start));
            var normalized = string.Concat(text.Where(char.IsLetterOrDigit)).ToUpperInvariant();
            if (normalized.Length > 0)
            {
                yield return (normalized, start, i, time);
            }

            start = i;
        }
    }

    private static bool IsWordStart(WindowTranscript window, int index) =>
        index == 0 || ParakeetVocab.IsWordBoundary(window.Tokens[index - 1], window.Tokens[index]);
}
