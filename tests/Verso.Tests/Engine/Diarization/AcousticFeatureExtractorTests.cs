using Verso.Core.Engine;
using Verso.Core.Engine.Diarization;

namespace Verso.Tests.Engine.Diarization;

public class AcousticFeatureExtractorTests
{
    private const int SampleRate = AcousticFeatureExtractor.SampleRate;

    [Fact]
    public void Extract_120HzTone_HasLowerPitchThan220HzTone()
    {
        var low = GenerateTone(seconds: 1.0, frequencyHz: 120, amplitude: 0.7f);
        var high = GenerateTone(seconds: 1.0, frequencyHz: 220, amplitude: 0.7f);

        var lowPitch = MeanVoicedPitch(AcousticFeatureExtractor.Extract(low));
        var highPitch = MeanVoicedPitch(AcousticFeatureExtractor.Extract(high));

        Assert.True(lowPitch > 80, $"pitch 120 Hz esperado ~120, obtido {lowPitch}");
        Assert.True(highPitch > lowPitch, $"220 Hz ({highPitch}) deveria ser > 120 Hz ({lowPitch})");
        Assert.InRange(lowPitch, 100, 140);
        Assert.InRange(highPitch, 200, 240);
    }

    [Fact]
    public void Extract_LouderTone_HasHigherLogEnergy()
    {
        var quiet = GenerateTone(seconds: 0.5, frequencyHz: 180, amplitude: 0.1f);
        var loud = GenerateTone(seconds: 0.5, frequencyHz: 180, amplitude: 0.9f);

        var quietEnergy = MeanLogEnergy(AcousticFeatureExtractor.Extract(quiet));
        var loudEnergy = MeanLogEnergy(AcousticFeatureExtractor.Extract(loud));

        Assert.True(loudEnergy > quietEnergy, $"energia alta ({loudEnergy}) deveria ser > baixa ({quietEnergy})");
    }

    [Fact]
    public void Extract_EmptyAudio_ReturnsEmpty()
    {
        Assert.Empty(AcousticFeatureExtractor.Extract([]));
    }

    private static float MeanVoicedPitch(AcousticFrame[] frames)
    {
        var voiced = frames.Where(f => f.PitchHz > 0).Select(f => f.PitchHz).ToList();
        Assert.NotEmpty(voiced);
        return voiced.Average();
    }

    private static float MeanLogEnergy(AcousticFrame[] frames)
    {
        Assert.NotEmpty(frames);
        return frames.Average(f => f.LogEnergy);
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
