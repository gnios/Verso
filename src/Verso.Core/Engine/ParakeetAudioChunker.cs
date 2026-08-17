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

    public static IReadOnlyList<TranscriptionSegmentResult> ShiftAndTrimOverlap(
        IReadOnlyList<TranscriptionSegmentResult> segments,
        double chunkStartSeconds,
        bool isFirstChunk,
        double overlapSeconds = OverlapSeconds)
    {
        if (segments.Count == 0)
        {
            return [];
        }

        var keepAfter = isFirstChunk ? 0 : overlapSeconds / 2.0;
        var shifted = new List<TranscriptionSegmentResult>(segments.Count);
        foreach (var segment in segments)
        {
            if (segment.StartSeconds < keepAfter)
            {
                continue;
            }

            shifted.Add(segment with
            {
                StartSeconds = segment.StartSeconds + chunkStartSeconds,
                EndSeconds = segment.EndSeconds + chunkStartSeconds,
            });
        }

        return shifted;
    }
}
