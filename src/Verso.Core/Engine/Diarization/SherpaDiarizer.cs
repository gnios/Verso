using SherpaOnnx;

namespace Verso.Core.Engine.Diarization;

public sealed class SherpaDiarizer(SherpaSpeakerModels models) : ILocalDiarizer
{
    public Task EnsureReadyAsync(CancellationToken cancellationToken) =>
        models.EnsureReadyAsync(cancellationToken);

    public IReadOnlyList<TranscriptionSegmentResult> Diarize(
        float[] audio, IReadOnlyList<TranscriptionSegmentResult> asrSegments) =>
        Diarize(audio, asrSegments, CancellationToken.None);

    public IReadOnlyList<TranscriptionSegmentResult> Diarize(
        float[] audio, IReadOnlyList<TranscriptionSegmentResult> asrSegments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (audio.Length == 0 || asrSegments.Count == 0)
            return asrSegments;

        var config = new OfflineSpeakerDiarizationConfig();
        config.Segmentation.Pyannote.Model = models.SegmentationPath;
        config.Segmentation.Pyannote.WindowShiftRatio = 0.1f;
        config.Segmentation.NumThreads = 4;
        config.Embedding.Model = models.EmbeddingPath;
        config.Embedding.NumThreads = 4;
        config.Clustering.NumClusters = 3;
        config.Clustering.Threshold = 0.5f;
        config.MinDurationOn = 0.3f;
        config.MinDurationOff = 0.5f;

        using var diarizer = new OfflineSpeakerDiarization(config);
        if (diarizer.SampleRate != AudioLoader.SampleRate)
            throw new InvalidOperationException("Taxa de amostragem incompatível com Sherpa ONNX.");

        OfflineSpeakerDiarizationProgressCallback progress =
            (_, _, _) => cancellationToken.IsCancellationRequested ? 1 : 0;
        var turns = diarizer.ProcessWithCallback(audio, progress, IntPtr.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        return AttributeWords(asrSegments, turns.Select(t => new SpeakerTurn(t.Start, t.End, t.Speaker)));
    }

    internal readonly record struct SpeakerTurn(double Start, double End, int Speaker);

    internal static IReadOnlyList<TranscriptionSegmentResult> AttributeWords(
        IReadOnlyList<TranscriptionSegmentResult> segments, IEnumerable<SpeakerTurn> turns)
    {
        var orderedTurns = turns.OrderBy(turn => turn.Start).ThenBy(turn => turn.Speaker).ToArray();
        var names = orderedTurns.Select(turn => turn.Speaker).Distinct()
            .Select((speaker, index) => (speaker, Name: $"Locutor {index + 1}"))
            .ToDictionary(item => item.speaker, item => item.Name);
        var result = new List<TranscriptionSegmentResult>();
        foreach (var segment in segments)
        {
            var words = segment.Words;
            if (words is not { Count: > 0 } ||
                string.Concat(words.Select(word => word.Text)).Trim() != segment.Text.Trim())
            {
                result.Add(segment with { SpeakerName = FindSpeaker(segment.StartSeconds, segment.EndSeconds) });
                continue;
            }

            var start = 0;
            var speaker = FindSpeaker(words[0].StartSeconds, words[0].EndSeconds);
            for (var index = 1; index < words.Count; index++)
            {
                var next = FindSpeaker(words[index].StartSeconds, words[index].EndSeconds);
                if (next == speaker)
                    continue;

                AddPiece(index);
                start = index;
                speaker = next;
            }

            AddPiece(words.Count);

            void AddPiece(int end)
            {
                if (start == 0 && end == words.Count)
                {
                    result.Add(segment with { SpeakerName = speaker });
                    return;
                }

                var piece = words.Skip(start).Take(end - start).ToArray();
                result.Add(new TranscriptionSegmentResult(
                    piece[0].StartSeconds, piece[^1].EndSeconds,
                    string.Concat(piece.Select(word => word.Text)).Trim(), speaker) { Words = piece });
            }
        }

        return result;

        string? FindSpeaker(double start, double end)
        {
            var overlapBySpeaker = new Dictionary<int, double>();
            foreach (var turn in orderedTurns)
            {
                if (turn.Start >= end)
                    break;

                var overlap = Math.Min(end, turn.End) - Math.Max(start, turn.Start);
                if (overlap > 0)
                    overlapBySpeaker[turn.Speaker] = overlapBySpeaker.GetValueOrDefault(turn.Speaker) + overlap;
            }

            return overlapBySpeaker.Count == 0
                ? null
                : names[overlapBySpeaker.MaxBy(pair => pair.Value).Key];
        }
    }
}
