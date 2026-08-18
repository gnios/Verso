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
    /// Recorte por token evita descartar a frase inteira quando ela começa no overlap.
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

        var count = Math.Min(timestamps.Count, tokens.Count);
        var trimmed = new List<TimedToken>(count);
        for (var i = 0; i < count; i++)
        {
            var t = timestamps[i];
            if (t < keepAfter || t >= keepUntil)
            {
                continue;
            }

            trimmed.Add(new TimedToken(t + chunkStartSeconds, tokens[i]));
        }

        return trimmed;
    }

    public static IReadOnlyList<TranscriptionSegmentResult> StitchWindowTokens(
        IReadOnlyList<WindowTranscript> windows)
    {
        if (windows.Count == 0)
        {
            return [];
        }

        var segments = new List<TranscriptionSegmentResult>();
        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            var trimmed = TrimOwnedRegion(
                window.Timestamps,
                window.Tokens,
                window.StartSeconds,
                isFirstChunk: i == 0,
                isLastChunk: i == windows.Count - 1,
                window.LengthSeconds);
            if (trimmed.Count == 0)
            {
                continue;
            }

            var times = new double[trimmed.Count];
            var toks = new string[trimmed.Count];
            for (var t = 0; t < trimmed.Count; t++)
            {
                times[t] = trimmed[t].TimeSeconds;
                toks[t] = trimmed[t].Text;
            }

            segments.AddRange(ParakeetSegmentBuilder.Build(string.Concat(toks), times, toks));
        }

        return segments;
    }
}
