namespace Verso.Core.Engine.Diarization;

public interface ILocalDiarizer
{
    Task EnsureReadyAsync(CancellationToken cancellationToken);

    IReadOnlyList<TranscriptionSegmentResult> Diarize(
        float[] audio,
        IReadOnlyList<TranscriptionSegmentResult> asrSegments);

    IReadOnlyList<TranscriptionSegmentResult> Diarize(
        float[] audio,
        IReadOnlyList<TranscriptionSegmentResult> asrSegments,
        CancellationToken cancellationToken) => Diarize(audio, asrSegments);
}
