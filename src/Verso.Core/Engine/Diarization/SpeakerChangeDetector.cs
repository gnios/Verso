namespace Verso.Core.Engine.Diarization;

public static class SpeakerChangeDetector
{
    public const double MinSegmentSeconds = 2.0;
    public const double MinTurnSeconds = 0.8;
    public const float AbsoluteDistanceThreshold = 0.8f;
    public const float PeakOverMedian = 1.2f;

    public static IReadOnlyList<double> DetectChangeTimes(
        AcousticFrame[] frames,
        double startSeconds,
        double endSeconds)
    {
        if (endSeconds - startSeconds < MinSegmentSeconds || frames.Length == 0)
        {
            return [];
        }

        var slice = new List<AcousticFrame>();
        foreach (var frame in frames)
        {
            if (frame.TimeSeconds >= startSeconds && frame.TimeSeconds < endSeconds)
            {
                slice.Add(frame);
            }
        }

        if (slice.Count < 8)
        {
            return [];
        }

        var duration = slice[^1].TimeSeconds - slice[0].TimeSeconds;
        if (duration < MinSegmentSeconds)
        {
            return [];
        }

        var vectors = new float[slice.Count][];
        for (var i = 0; i < slice.Count; i++)
        {
            vectors[i] = FrameVector(slice[i]);
        }

        ZNormalize(vectors);

        var distances = new List<(int Index, float Distance)>();
        for (var i = 1; i < slice.Count; i++)
        {
            var leftDur = slice[i].TimeSeconds - slice[0].TimeSeconds;
            var rightDur = slice[^1].TimeSeconds - slice[i].TimeSeconds;
            if (leftDur < MinTurnSeconds || rightDur < MinTurnSeconds)
            {
                continue;
            }

            var left = Mean(vectors, 0, i);
            var right = Mean(vectors, i, vectors.Length);
            distances.Add((i, Euclidean(left, right)));
        }

        if (distances.Count == 0)
        {
            return [];
        }

        var best = distances[0];
        foreach (var candidate in distances)
        {
            if (candidate.Distance > best.Distance)
            {
                best = candidate;
            }
        }

        var median = Median(distances.Select(d => d.Distance).ToArray());
        if (best.Distance < AbsoluteDistanceThreshold || best.Distance < PeakOverMedian * Math.Max(median, 1e-6f))
        {
            return [];
        }

        return [slice[best.Index].TimeSeconds];
    }

    private static float[] FrameVector(AcousticFrame frame)
    {
        var vector = new float[AcousticFeatureExtractor.MfccCount + 1];
        Array.Copy(frame.Mfcc, vector, AcousticFeatureExtractor.MfccCount);
        vector[AcousticFeatureExtractor.MfccCount] = frame.PitchHz;
        return vector;
    }

    private static void ZNormalize(float[][] vectors)
    {
        var dim = vectors[0].Length;
        var n = vectors.Length;
        for (var d = 0; d < dim; d++)
        {
            var mean = 0.0;
            for (var i = 0; i < n; i++)
            {
                mean += vectors[i][d];
            }

            mean /= n;
            var var = 0.0;
            for (var i = 0; i < n; i++)
            {
                var delta = vectors[i][d] - mean;
                var += delta * delta;
            }

            var std = Math.Sqrt(var / n);
            if (std < 1e-8)
            {
                std = 1;
            }

            for (var i = 0; i < n; i++)
            {
                vectors[i][d] = (float)((vectors[i][d] - mean) / std);
            }
        }
    }

    private static float[] Mean(float[][] vectors, int from, int to)
    {
        var dim = vectors[0].Length;
        var mean = new float[dim];
        var count = to - from;
        if (count <= 0)
        {
            return mean;
        }

        for (var i = from; i < to; i++)
        {
            for (var d = 0; d < dim; d++)
            {
                mean[d] += vectors[i][d];
            }
        }

        var inv = 1f / count;
        for (var d = 0; d < dim; d++)
        {
            mean[d] *= inv;
        }

        return mean;
    }

    private static float Euclidean(float[] a, float[] b)
    {
        var sum = 0.0;
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }

        return (float)Math.Sqrt(sum);
    }

    private static float Median(float[] values)
    {
        Array.Sort(values);
        var mid = values.Length / 2;
        if (values.Length % 2 == 1)
        {
            return values[mid];
        }

        return (values[mid - 1] + values[mid]) / 2f;
    }
}
