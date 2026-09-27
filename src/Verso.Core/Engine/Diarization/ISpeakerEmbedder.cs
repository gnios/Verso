namespace Verso.Core.Engine.Diarization;

public interface ISpeakerEmbedder
{
    Task EnsureReadyAsync(CancellationToken cancellationToken);

    float[] Embed(ReadOnlySpan<float> samples16kHz);
}
