namespace Verso.Core.Engine.Diarization;

public interface ILocalDiarizer
{
    Task EnsureReadyAsync(CancellationToken cancellationToken);

    IReadOnlyList<TranscriptionSegmentResult> Diarize(
        float[] audio,
        IReadOnlyList<TranscriptionSegmentResult> asrSegments);
}
