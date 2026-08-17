using Verso.Core.Engine;

namespace Verso.Tests.Engine;

public class ParakeetVocabTests
{
    [Fact]
    public void Load_ParsesTokenIdsAndTreatsUnderscoreAsSpace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"verso-vocab-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "a 0\n\u2581 1\nb 2\n<blk> 3\n");
        try
        {
            var vocab = ParakeetVocab.Load(path);
            Assert.Equal(4, vocab.Size);
            Assert.Equal(3, vocab.BlankIndex);
            Assert.Equal("a", vocab[0]);
            Assert.Equal(" ", vocab[1]);
            Assert.Equal("a b", vocab.Join([0, 1, 2]));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ParakeetSegmentBuilderTests
{
    [Fact]
    public void Build_EmptyTokens_ReturnsEmptyList()
    {
        Assert.Empty(ParakeetSegmentBuilder.Build("", [], []));
    }

    [Fact]
    public void Build_SplitsWhenPauseIsAtLeastDefault()
    {
        var tokens = new[] { "olá ", "mundo", " depois" };
        var timestamps = new[] { 0.0, 0.16, 1.0 };
        var segments = ParakeetSegmentBuilder.Build("olá mundo depois", timestamps, tokens);

        Assert.Equal(2, segments.Count);
        Assert.Equal("olá mundo", segments[0].Text);
        Assert.Equal(0, segments[0].StartSeconds, precision: 2);
        Assert.Equal("depois", segments[1].Text);
        Assert.Equal(1.0, segments[1].StartSeconds, precision: 2);
        Assert.True(segments[1].EndSeconds > segments[1].StartSeconds);
    }
}

public class ParakeetTdtGreedyTests
{
    [Fact]
    public void Decode_EmitsNonBlankTokensAndAdvancesDuration()
    {
        var frames = new[]
        {
            new float[] { 0 },
            new float[] { 1 },
            new float[] { 2 },
        };

        var (tokens, tokenFrames) = ParakeetTdtGreedy.Decode(
            frames,
            blankIndex: 0,
            vocabSize: 3,
            initialState: 0,
            decode: (prev, state, frame) =>
            {
                var t = (int)frame[0];
                if (t == 0)
                {
                    return ([10f, 1f, 0f], 1, state);
                }

                if (t == 1)
                {
                    return ([0f, 5f, 1f], 1, state + 1);
                }

                return ([8f, 0f, 0f], 1, state);
            });

        Assert.Equal([1], tokens);
        Assert.Equal([1], tokenFrames);
    }

    [Fact]
    public void ArgMax_ReturnsHighestIndexInRange()
    {
        Assert.Equal(2, ParakeetTdtGreedy.ArgMax([0.1f, 0.2f, 0.9f, 0.4f], 3));
    }
}

public class WordErrorRateTests
{
    [Fact]
    public void Compute_IdenticalTranscripts_IsZero()
    {
        Assert.Equal(0, WordErrorRate.Compute("olá mundo", "olá mundo"));
    }

    [Fact]
    public void Compute_OneSubstitutionInTwoWords_IsHalf()
    {
        Assert.Equal(0.5, WordErrorRate.Compute("olá mundo", "olá terra"), precision: 4);
    }

    [Fact]
    public void Compute_IgnoresPunctuationAndCasing()
    {
        Assert.Equal(0, WordErrorRate.Compute(
            "olá este é um teste",
            "Olá, este é um teste."));
    }
}
