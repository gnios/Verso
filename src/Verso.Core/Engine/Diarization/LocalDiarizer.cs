namespace Verso.Core.Engine.Diarization;

public sealed class LocalDiarizer : ILocalDiarizer
{
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

        var frames = AcousticFeatureExtractor.Extract(audio);
        var pieces = SplitOnSpeakerChanges(asrSegments, frames);
        var embeddings = new float[pieces.Count][];
        for (var i = 0; i < pieces.Count; i++)
        {
            embeddings[i] = AcousticFeatureExtractor.MeanEmbedding(
                FramesInRange(frames, pieces[i].StartSeconds, pieces[i].EndSeconds));
        }

        var assembled = SpeakerTurnAssembler.Assemble(pieces, embeddings);
        if (assembled.All(s => s.SpeakerName == SpeakerTurnAssembler.SpeakerOne)
            && assembled.Count != asrSegments.Count)
        {
            return LabelAll(asrSegments, SpeakerTurnAssembler.SpeakerOne);
        }

        return assembled;
    }

    private static List<TranscriptionSegmentResult> SplitOnSpeakerChanges(
        IReadOnlyList<TranscriptionSegmentResult> asrSegments,
        AcousticFrame[] frames)
    {
        var pieces = new List<TranscriptionSegmentResult>();
        foreach (var segment in asrSegments)
        {
            var changes = SpeakerChangeDetector.DetectChangeTimes(
                frames,
                segment.StartSeconds,
                segment.EndSeconds);
            if (changes.Count == 0)
            {
                pieces.Add(segment);
                continue;
            }

            var cursor = segment.StartSeconds;
            var remaining = segment.Text;
            foreach (var change in changes.OrderBy(t => t))
            {
                if (change <= cursor || change >= segment.EndSeconds)
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
                    pieces.Add(new TranscriptionSegmentResult(cursor, change, left));
                }

                remaining = right;
                cursor = change;
            }

            if (remaining.Length > 0 || pieces.Count == 0)
            {
                pieces.Add(new TranscriptionSegmentResult(cursor, segment.EndSeconds, remaining));
            }
        }

        return pieces;
    }

    private static List<AcousticFrame> FramesInRange(
        AcousticFrame[] frames,
        double startSeconds,
        double endSeconds)
    {
        var slice = new List<AcousticFrame>();
        foreach (var frame in frames)
        {
            if (frame.TimeSeconds >= startSeconds && frame.TimeSeconds < endSeconds)
            {
                slice.Add(frame);
            }
        }

        return slice;
    }

    private static IReadOnlyList<TranscriptionSegmentResult> LabelAll(
        IReadOnlyList<TranscriptionSegmentResult> segments,
        string speakerName) =>
        segments.Select(s => s with { SpeakerName = speakerName }).ToList();
}
