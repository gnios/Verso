using Verso.Core.Services;

namespace Verso.Core.Engine.Diarization;

public static class SpeakerTurnAssembler
{
    public const string SpeakerOne = "Locutor 1";
    public const string SpeakerTwo = "Locutor 2";
    public const float SeparabilityRatio = 1.5f;
    public const float MinCentroidDistance = 40f;
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

        var (i0, i1) = FarthestPair(embeddings);
        if (i0 == i1)
        {
            return labels;
        }

        var c0 = (float[])embeddings[i0].Clone();
        var c1 = (float[])embeddings[i1].Clone();

        for (var iter = 0; iter < KmeansIterations; iter++)
        {
            for (var i = 0; i < n; i++)
            {
                labels[i] = EuclideanSq(embeddings[i], c0) <= EuclideanSq(embeddings[i], c1) ? 0 : 1;
            }

            if (!TryCentroid(embeddings, labels, 0, c0) || !TryCentroid(embeddings, labels, 1, c1))
            {
                return new int[n];
            }
        }

        var n0 = labels.Count(l => l == 0);
        var n1 = n - n0;
        if (n0 == 0 || n1 == 0)
        {
            return new int[n];
        }

        var intra = MeanIntra(embeddings, labels, c0, c1);
        var between = MathF.Sqrt(EuclideanSq(c0, c1));
        separable = between >= MinCentroidDistance
            && between > SeparabilityRatio * Math.Max(intra, 1e-6f);
        if (!separable)
        {
            Array.Clear(labels);
        }

        return labels;
    }

    private static IReadOnlyList<TranscriptionSegmentResult> MergeConsecutive(
        IReadOnlyList<TranscriptionSegmentResult> named)
    {
        var merged = new List<TranscriptionSegmentResult>();
        foreach (var piece in named)
        {
            if (merged.Count > 0
                && merged[^1].SpeakerName == piece.SpeakerName)
            {
                var prev = merged[^1];
                merged[^1] = prev with
                {
                    EndSeconds = piece.EndSeconds,
                    Text = SegmentEditingService.JoinTexts(prev.Text, piece.Text),
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
        var best = -1f;
        for (var i = 0; i < embeddings.Count; i++)
        {
            for (var j = i + 1; j < embeddings.Count; j++)
            {
                var d = EuclideanSq(embeddings[i], embeddings[j]);
                if (d > best)
                {
                    best = d;
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

    private static float MeanIntra(
        IReadOnlyList<float[]> embeddings,
        int[] labels,
        float[] c0,
        float[] c1)
    {
        var sum = 0.0;
        for (var i = 0; i < embeddings.Count; i++)
        {
            var c = labels[i] == 0 ? c0 : c1;
            sum += Math.Sqrt(EuclideanSq(embeddings[i], c));
        }

        return (float)(sum / embeddings.Count);
    }

    private static float EuclideanSq(float[] a, float[] b)
    {
        var sum = 0f;
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }

        return sum;
    }
}
