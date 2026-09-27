using Verso.Core.Engine;
using Verso.Core.Engine.Diarization;

namespace Verso.Tests.Engine.Diarization;

public class LocalDiarizerTests
{
    private const int SampleRate = AcousticFeatureExtractor.SampleRate;
    private readonly LocalDiarizer _diarizer = new(new PitchSplitSpeakerEmbedder());

    [Fact]
    public void Diarize_TwoTonesTwoAsrSegments_ReturnsTwoTurns()
    {
        var audio = Concat(GenerateTone(3, 120, 0.7f), GenerateTone(3, 220, 0.7f));
        var asr = new[]
        {
            new TranscriptionSegmentResult(0, 3, "fala um"),
            new TranscriptionSegmentResult(3, 6, "fala dois"),
        };

        var result = _diarizer.Diarize(audio, asr);

        Assert.Equal(2, result.Count);
        Assert.Equal("Locutor 1", result[0].SpeakerName);
        Assert.Equal("Locutor 2", result[1].SpeakerName);
        Assert.Equal("fala um", result[0].Text);
        Assert.Equal("fala dois", result[1].Text);
        Assert.Equal(0, result[0].StartSeconds);
        Assert.Equal(3, result[0].EndSeconds);
        Assert.Equal(3, result[1].StartSeconds);
        Assert.Equal(6, result[1].EndSeconds);
    }

    [Fact]
    public void Diarize_ConsecutiveSameSpeaker_MergesTextWithSpace()
    {
        var audio = Concat(
            GenerateTone(2, 120, 0.7f),
            GenerateTone(2, 120, 0.7f),
            GenerateTone(2, 220, 0.7f));
        var asr = new[]
        {
            new TranscriptionSegmentResult(0, 2, "um"),
            new TranscriptionSegmentResult(2, 4, "dois"),
            new TranscriptionSegmentResult(4, 6, "tres"),
        };

        var result = _diarizer.Diarize(audio, asr);

        Assert.Equal(2, result.Count);
        Assert.Equal("Locutor 1", result[0].SpeakerName);
        Assert.Equal("Locutor 2", result[1].SpeakerName);
        Assert.Equal("um dois", result[0].Text);
        Assert.Equal("tres", result[1].Text);
        Assert.Equal(0, result[0].StartSeconds);
        Assert.Equal(4, result[0].EndSeconds);
    }

    [Fact]
    public void Diarize_SingleTone_KeepsAsrCutsWithLocutor1()
    {
        var audio = GenerateTone(6, 180, 0.7f);
        var asr = new[]
        {
            new TranscriptionSegmentResult(0, 3, "parte a"),
            new TranscriptionSegmentResult(3, 6, "parte b"),
        };

        var result = _diarizer.Diarize(audio, asr);

        Assert.Equal(2, result.Count);
        Assert.All(result, s => Assert.Equal("Locutor 1", s.SpeakerName));
        Assert.Equal("parte a", result[0].Text);
        Assert.Equal("parte b", result[1].Text);
        Assert.DoesNotContain(result, s => s.SpeakerName == "Locutor 2");
    }

    [Fact]
    public void Diarize_OneAsrSegmentSpanningTwoVoices_SplitsOnWordBoundary()
    {
        var audio = Concat(GenerateTone(3, 120, 0.7f), GenerateTone(3, 220, 0.7f));
        var asr = new[]
        {
            new TranscriptionSegmentResult(0, 6, "olá mundo. como vai você"),
        };

        var result = _diarizer.Diarize(audio, asr);

        Assert.Equal(2, result.Count);
        Assert.Equal("Locutor 1", result[0].SpeakerName);
        Assert.Equal("Locutor 2", result[1].SpeakerName);
        Assert.Equal("olá mundo.", result[0].Text);
        Assert.Equal("como vai você", result[1].Text);
        Assert.Equal(0, result[0].StartSeconds);
        Assert.InRange(result[0].EndSeconds, 2.5, 3.5);
        Assert.InRange(result[1].StartSeconds, 2.5, 3.5);
        Assert.Equal(6, result[1].EndSeconds);
        Assert.Equal(result[0].EndSeconds, result[1].StartSeconds);
    }

    [Fact]
    public void Diarize_EmptySegments_ReturnsEmpty()
    {
        Assert.Empty(_diarizer.Diarize(GenerateTone(1, 180, 0.5f), []));
    }

    private static float[] Concat(params float[][] parts)
    {
        var audio = new float[parts.Sum(p => p.Length)];
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
