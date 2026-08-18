using Verso.Core.Engine;
using Verso.Core.Engine.Diarization;

namespace Verso.Tests.Engine.Diarization;

public class SpeakerChangeDetectorTests
{
    private const int SampleRate = AcousticFeatureExtractor.SampleRate;

    [Fact]
    public void DetectChangeTimes_TwoTonesInLongSegment_SplitsNearBoundary()
    {
        var audio = Concat(
            GenerateTone(3.0, 120, 0.7f),
            GenerateTone(3.0, 220, 0.7f));
        var frames = AcousticFeatureExtractor.Extract(audio);

        var changes = SpeakerChangeDetector.DetectChangeTimes(frames, 0, 6);

        Assert.Single(changes);
        Assert.InRange(changes[0], 2.5, 3.5);
    }

    [Fact]
    public void DetectChangeTimes_SegmentShorterThanTwoSeconds_DoesNotSplit()
    {
        var audio = Concat(
            GenerateTone(0.8, 120, 0.7f),
            GenerateTone(0.8, 220, 0.7f));
        var frames = AcousticFeatureExtractor.Extract(audio);

        var changes = SpeakerChangeDetector.DetectChangeTimes(frames, 0, 1.6);

        Assert.Empty(changes);
    }

    [Fact]
    public void DetectChangeTimes_SingleTone_DoesNotSplit()
    {
        var audio = GenerateTone(4.0, 180, 0.7f);
        var frames = AcousticFeatureExtractor.Extract(audio);

        var changes = SpeakerChangeDetector.DetectChangeTimes(frames, 0, 4);

        Assert.Empty(changes);
    }

    private static float[] Concat(params float[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var audio = new float[total];
        var offset = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, audio, offset, part.Length);
            offset += part.Length;
        }

        return audio;
    }

    private static float[] GenerateTone(double seconds, float frequencyHz, float amplitude)
    {
        var length = (int)(seconds * SampleRate);
        var audio = new float[length];
        for (var i = 0; i < length; i++)
        {
            audio[i] = amplitude * MathF.Sin(2 * MathF.PI * frequencyHz * i / SampleRate);
        }

        return audio;
    }
}
