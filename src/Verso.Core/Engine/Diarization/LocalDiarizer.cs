namespace Verso.Core.Engine.Diarization;

public sealed class LocalDiarizer : ILocalDiarizer
{
    public const double WindowSeconds = 1.5;
    public const double HopSeconds = 0.5;
    public const double MinWindowSeconds = 0.8;
    public const double MinTurnSeconds = 0.8;

    private readonly ISpeakerEmbedder _embedder;

    public LocalDiarizer(ISpeakerEmbedder embedder)
    {
        _embedder = embedder;
    }

    public Task EnsureReadyAsync(CancellationToken cancellationToken) =>
        _embedder.EnsureReadyAsync(cancellationToken);

    public IReadOnlyList<TranscriptionSegmentResult> Diarize(
        float[] audio,
        IReadOnlyList<TranscriptionSegmentResult> asrSegments)
    {
        if (asrSegments.Count == 0)
        {
            return [];
        }

        if (audio.Length == 0)
        {
            return LabelAll(asrSegments, SpeakerTurnAssembler.SpeakerOne);
        }

        var windows = CollectWindows(audio, asrSegments);
        if (windows.Count < 2)
        {
            return LabelAll(asrSegments, SpeakerTurnAssembler.SpeakerOne);
        }

        var embeddings = windows.Select(w => w.Embedding).ToList();
        var labels = SpeakerTurnAssembler.ClusterTwo(embeddings, out var separable);
        if (!separable)
        {
            return LabelAll(asrSegments, SpeakerTurnAssembler.SpeakerOne);
        }

        var firstLabel = labels[0];
        var pieces = new List<TranscriptionSegmentResult>();
        foreach (var segment in asrSegments)
        {
            pieces.AddRange(SplitSegmentByWindowLabels(segment, windows, labels, firstLabel));
        }

        return SpeakerTurnAssembler.MergeConsecutive(pieces);
    }

    private List<SpeechWindow> CollectWindows(
        float[] audio,
        IReadOnlyList<TranscriptionSegmentResult> asrSegments)
    {
        var sampleRate = AudioLoader.SampleRate;
        var windows = new List<SpeechWindow>();
        foreach (var segment in asrSegments)
        {
            for (var t = segment.StartSeconds;
                 t + MinWindowSeconds <= segment.EndSeconds;
                 t += HopSeconds)
            {
                var length = Math.Min(WindowSeconds, segment.EndSeconds - t);
                if (length < MinWindowSeconds)
                {
                    break;
                }

                var startSample = Math.Clamp((int)Math.Round(t * sampleRate), 0, audio.Length);
                var endSample = Math.Clamp(
                    (int)Math.Round((t + length) * sampleRate),
                    startSample,
                    audio.Length);
                if (endSample - startSample < sampleRate * MinWindowSeconds / 2)
                {
                    continue;
                }

                var slice = audio.AsSpan(startSample, endSample - startSample);
                windows.Add(new SpeechWindow(t, t + length, _embedder.Embed(slice)));
            }
        }

        return windows;
    }

    private static IEnumerable<TranscriptionSegmentResult> SplitSegmentByWindowLabels(
        TranscriptionSegmentResult segment,
        List<SpeechWindow> windows,
        int[] labels,
        int firstLabel)
    {
        var overlapping = new List<(SpeechWindow Window, int Label)>();
        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            if (window.EndSeconds <= segment.StartSeconds || window.StartSeconds >= segment.EndSeconds)
            {
                continue;
            }

            overlapping.Add((window, labels[i]));
        }

        if (overlapping.Count == 0)
        {
            yield return segment with { SpeakerName = SpeakerTurnAssembler.SpeakerOne };
            yield break;
        }

        overlapping.Sort((a, b) => a.Window.StartSeconds.CompareTo(b.Window.StartSeconds));
        var cursor = segment.StartSeconds;
        var remaining = segment.Text;
        var currentLabel = overlapping[0].Label;
        for (var i = 1; i < overlapping.Count; i++)
        {
            if (overlapping[i].Label == currentLabel)
            {
                continue;
            }

            var change = overlapping[i].Window.StartSeconds;
            if (change - cursor < MinTurnSeconds
                || segment.EndSeconds - change < MinTurnSeconds)
            {
                continue;
            }

            var (left, right) = WordBoundaryTextSplitter.Split(
                remaining,
                cursor,
                segment.EndSeconds,
                change);
            if (left.Length > 0)
            {
                yield return new TranscriptionSegmentResult(
                    cursor,
                    change,
                    left,
                    NameFor(currentLabel, firstLabel));
            }

            remaining = right;
            cursor = change;
            currentLabel = overlapping[i].Label;
        }

        if (remaining.Length > 0 || cursor == segment.StartSeconds)
        {
            yield return new TranscriptionSegmentResult(
                cursor,
                segment.EndSeconds,
                remaining.Length > 0 ? remaining : segment.Text,
                NameFor(currentLabel, firstLabel));
        }
    }

    private static string NameFor(int label, int firstLabel) =>
        label == firstLabel ? SpeakerTurnAssembler.SpeakerOne : SpeakerTurnAssembler.SpeakerTwo;

    private static IReadOnlyList<TranscriptionSegmentResult> LabelAll(
        IReadOnlyList<TranscriptionSegmentResult> segments,
        string speakerName) =>
        segments.Select(s => s with { SpeakerName = speakerName }).ToList();

    private readonly record struct SpeechWindow(double StartSeconds, double EndSeconds, float[] Embedding);
}
