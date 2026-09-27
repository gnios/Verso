using Verso.Core.Engine.Diarization;

namespace Verso.Tests.Engine.Diarization;

/// <summary>
/// Stub de teste: 120 Hz vs 220 Hz viram dois vetores ortogonais, sem baixar o ECAPA.
/// </summary>
public sealed class PitchSplitSpeakerEmbedder : ISpeakerEmbedder
{
    public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public float[] Embed(ReadOnlySpan<float> samples16kHz)
    {
        var pitch = AcousticFeatureExtractor.EstimatePitch(samples16kHz, AcousticFeatureExtractor.SampleRate);
        var vector = new float[2];
        if (pitch > 0 && pitch >= 160)
        {
            vector[1] = 1;
        }
        else
        {
            vector[0] = 1;
        }

        return vector;
    }
}
