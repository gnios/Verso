using Verso.Core.Data.Entities;
using Verso.Core.Engine;

namespace Verso.Tests.Engine;

public class SilenceSplitterTests
{
    private const int SampleRate = SilenceSplitter.SampleRate;

    [Fact]
    public void SplitBySilence_WithEmptyAudio_ReturnsEmptyList()
    {
        var result = SilenceSplitter.SplitBySilence([]);

        Assert.Empty(result);
    }

    [Fact]
    public void SplitBySilence_WithAllSilence_ReturnsSingleFallbackChunk()
    {
        var audio = new float[SampleRate * 2];

        var result = SilenceSplitter.SplitBySilence(audio);

        Assert.Single(result);
        Assert.Equal(0, result[0].OffsetSec);
        Assert.Equal(audio.Length, result[0].Samples.Length);
    }

    [Fact]
    public void SplitBySilence_WithTwoSpeechBlocksSeparatedBySilence_ReturnsTwoChunksWithCorrectOffsets()
    {
        var audio = BuildTwoSpeechBlocksWithSilenceGap(
            speechSeconds: 0.3,
            silenceSeconds: 0.7,
            speechSeconds2: 0.3,
            amplitude: 0.8f);

        var result = SilenceSplitter.SplitBySilence(audio);

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].OffsetSec, precision: 2);
        Assert.True(result[1].OffsetSec > 0.5, $"Offset esperado após silêncio, obtido {result[1].OffsetSec}");
        Assert.True(result[0].Samples.Length > 0);
        Assert.True(result[1].Samples.Length > 0);
    }

    [Fact]
    public void SplitBySilence_WithContinuousSpeech_ReturnsSingleChunkStartingAtZero()
    {
        var audio = GenerateTone(SampleRate, seconds: 1.0, frequencyHz: 440, amplitude: 0.7f);

        var result = SilenceSplitter.SplitBySilence(audio);

        Assert.Single(result);
        Assert.Equal(0, result[0].OffsetSec);
        Assert.Equal(audio.Length, result[0].Samples.Length);
    }

    private static float[] BuildTwoSpeechBlocksWithSilenceGap(
        double speechSeconds,
        double silenceSeconds,
        double speechSeconds2,
        float amplitude)
    {
        var speechSamples = (int)(speechSeconds * SampleRate);
        var silenceSamples = (int)(silenceSeconds * SampleRate);
        var speechSamples2 = (int)(speechSeconds2 * SampleRate);
        var total = speechSamples + silenceSamples + speechSamples2;
        var audio = new float[total];
        FillTone(audio, 0, speechSamples, frequencyHz: 440, amplitude);
        FillTone(audio, speechSamples + silenceSamples, speechSamples2, frequencyHz: 880, amplitude);
        return audio;
    }

    private static float[] GenerateTone(int sampleRate, double seconds, float frequencyHz, float amplitude)
    {
        var length = (int)(seconds * sampleRate);
        var audio = new float[length];
        FillTone(audio, 0, length, frequencyHz, amplitude);
        return audio;
    }

    private static void FillTone(float[] buffer, int start, int length, float frequencyHz, float amplitude)
    {
        for (var i = 0; i < length; i++)
        {
            var t = (start + i) / (float)SampleRate;
            buffer[start + i] = amplitude * MathF.Sin(2 * MathF.PI * frequencyHz * t);
        }
    }
}
