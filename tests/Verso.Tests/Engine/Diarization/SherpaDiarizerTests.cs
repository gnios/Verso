using Verso.Core.Engine;
using Verso.Core.Engine.Diarization;

namespace Verso.Tests.Engine.Diarization;

public class SherpaDiarizerTests
{
    [Fact]
    public void AttributeWords_UsesOverlapForThreeSpeakersAndPreservesUnattributedSpeech()
    {
        TranscriptionWordTiming[] words =
        [
            new("Primeira ", 0.1, 0.8),
            new("segunda ", 1.2, 1.7),
            new("terceira ", 2.3, 2.8),
            new("fim.", 3.3, 3.7),
        ];
        TranscriptionSegmentResult[] segments =
        [
            new(0.1, 3.7, "Primeira segunda terceira fim.") { Words = words },
        ];
        SherpaDiarizer.SpeakerTurn[] turns = [new(2, 3, 8), new(0, 1.4, 4), new(1.25, 2, 6)];

        var result = SherpaDiarizer.AttributeWords(segments, turns);

        Assert.Equal(new string?[] { "Locutor 1", "Locutor 2", "Locutor 3", null },
            result.Select(segment => segment.SpeakerName));
        Assert.Equal(words, result.SelectMany(segment => segment.Words!));
        Assert.Equal(segments[0].Text, string.Join(" ", result.Select(segment => segment.Text)));
    }

    [Fact]
    public void AttributeWords_UsesSegmentOverlapWhenWordTimingsAreUnavailable()
    {
        TranscriptionSegmentResult[] segments = [new(1, 4, "Texto original.")];
        SherpaDiarizer.SpeakerTurn[] turns = [new(0, 1.2, 5), new(1.2, 4, 7)];

        var segment = Assert.Single(SherpaDiarizer.AttributeWords(segments, turns));

        Assert.Equal(segments[0] with { SpeakerName = "Locutor 2" }, segment);
    }
}
