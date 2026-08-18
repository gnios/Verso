namespace Verso.Core.Engine.Diarization;

public interface ILocalDiarizer
{
    IReadOnlyList<TranscriptionSegmentResult> Diarize(
        float[] audio,
        IReadOnlyList<TranscriptionSegmentResult> asrSegments);
}
