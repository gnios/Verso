using Verso.Core.Services;

namespace Verso.Core.Engine.Diarization;

public static class SpeakerTurnAssembler
{
    public const string SpeakerOne = "Locutor 1";
    public const string SpeakerTwo = "Locutor 2";
    public const float MaxCentroidCosine = 0.5f;
    public const float SeparabilityRatio = 1.5f;
    private const int KmeansIterations = 24;

    public static IReadOnlyList<TranscriptionSegmentResult> Assemble(
        IReadOnlyList<TranscriptionSegmentResult> pieces,
        IReadOnlyList<float[]> embeddings)
    {
        if (pieces.Count == 0)
        {
            return [];
        }

        var labels = ClusterTwo(embeddings, out var separable);
        if (!separable)
        {
            return pieces
                .Select(p => p with { SpeakerName = SpeakerOne })
                .ToList();
        }

        var firstLabel = labels[0];
        var named = new List<TranscriptionSegmentResult>(pieces.Count);
        for (var i = 0; i < pieces.Count; i++)
        {
            var name = labels[i] == firstLabel ? SpeakerOne : SpeakerTwo;
            named.Add(pieces[i] with { SpeakerName = name });
        }

        return MergeConsecutive(named);
    }

    internal static int[] ClusterTwo(IReadOnlyList<float[]> embeddings, out bool separable)
    {
        var n = embeddings.Count;
        var labels = new int[n];
        separable = false;
        if (n < 2)
        {
            return labels;
        }

        var unit = new float[n][];
        for (var i = 0; i < n; i++)
        {
            unit[i] = L2Normalize(embeddings[i]);
        }

        var (i0, i1) = FarthestPair(unit);
        if (i0 == i1 || Cosine(unit[i0], unit[i1]) >= 0.999f)
        {
            return labels;
        }

        var c0 = (float[])unit[i0].Clone();
        var c1 = (float[])unit[i1].Clone();

        for (var iter = 0; iter < KmeansIterations; iter++)
        {
            for (var i = 0; i < n; i++)
            {
                labels[i] = Cosine(unit[i], c0) >= Cosine(unit[i], c1) ? 0 : 1;
            }

            if (!TryCentroid(unit, labels, 0, c0) || !TryCentroid(unit, labels, 1, c1))
            {
                return new int[n];
            }

            L2NormalizeInPlace(c0);
            L2NormalizeInPlace(c1);
        }

        var n0 = labels.Count(l => l == 0);
        var n1 = n - n0;
        if (n0 == 0 || n1 == 0)
        {
            return new int[n];
        }

        var intra = MeanIntraDistance(unit, labels, c0, c1);
        var between = 1f - Cosine(c0, c1);
        separable = Cosine(c0, c1) <= MaxCentroidCosine
            && between > SeparabilityRatio * Math.Max(intra, 1e-6f);
        if (!separable)
        {
            Array.Clear(labels);
        }

        return labels;
    }

    internal static IReadOnlyList<TranscriptionSegmentResult> MergeConsecutive(
        IReadOnlyList<TranscriptionSegmentResult> named)
    {
        var merged = new List<TranscriptionSegmentResult>();
        foreach (var piece in named)
        {
            if (merged.Count > 0
                && merged[^1].SpeakerName == piece.SpeakerName
                && piece.StartSeconds - merged[^1].EndSeconds < ParakeetSegmentBuilder.DefaultPauseSeconds
                && piece.EndSeconds - merged[^1].StartSeconds <= ParakeetSegmentBuilder.MaxSegmentSeconds)
            {
                var prev = merged[^1];
                merged[^1] = prev with
                {
                    EndSeconds = piece.EndSeconds,
                    Text = SegmentEditingService.JoinTexts(prev.Text, piece.Text),
                    Words = prev.Words is not null && piece.Words is not null
                        ? [.. prev.Words, .. piece.Words]
                        : null,
                };
            }
            else
            {
                merged.Add(piece);
            }
        }

        return merged;
    }

    private static (int, int) FarthestPair(IReadOnlyList<float[]> embeddings)
    {
        var bestA = 0;
        var bestB = 0;
        var best = float.MaxValue;
        for (var i = 0; i < embeddings.Count; i++)
        {
            for (var j = i + 1; j < embeddings.Count; j++)
            {
                var cosine = Cosine(embeddings[i], embeddings[j]);
                if (cosine < best)
                {
                    best = cosine;
                    bestA = i;
                    bestB = j;
                }
            }
        }

        return (bestA, bestB);
    }

    private static bool TryCentroid(
        IReadOnlyList<float[]> embeddings,
        int[] labels,
        int label,
        float[] centroid)
    {
        var dim = centroid.Length;
        Array.Clear(centroid);
        var count = 0;
        for (var i = 0; i < embeddings.Count; i++)
        {
            if (labels[i] != label)
            {
                continue;
            }

            var v = embeddings[i];
            for (var d = 0; d < dim; d++)
            {
                centroid[d] += v[d];
            }

            count++;
        }

        if (count == 0)
        {
            return false;
        }

        var inv = 1f / count;
        for (var d = 0; d < dim; d++)
        {
            centroid[d] *= inv;
        }

        return true;
    }

    private static float MeanIntraDistance(
        IReadOnlyList<float[]> embeddings,
        int[] labels,
        float[] c0,
        float[] c1)
    {
        var sum = 0.0;
        for (var i = 0; i < embeddings.Count; i++)
        {
            var c = labels[i] == 0 ? c0 : c1;
            sum += 1f - Cosine(embeddings[i], c);
        }

        return (float)(sum / embeddings.Count);
    }

    internal static float Cosine(float[] a, float[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        var dot = 0f;
        for (var i = 0; i < n; i++)
        {
            dot += a[i] * b[i];
        }

        return Math.Clamp(dot, -1f, 1f);
    }

    internal static float[] L2Normalize(float[] vector)
    {
        var copy = (float[])vector.Clone();
        L2NormalizeInPlace(copy);
        return copy;
    }

    private static void L2NormalizeInPlace(float[] vector)
    {
        var sumSq = 0f;
        foreach (var v in vector)
        {
            sumSq += v * v;
        }

        var norm = MathF.Sqrt(sumSq);
        if (norm < 1e-8f)
        {
            return;
        }

        var inv = 1f / norm;
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] *= inv;
        }
    }
}
