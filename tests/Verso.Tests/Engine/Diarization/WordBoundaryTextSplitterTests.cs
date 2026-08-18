using Verso.Core.Engine.Diarization;

namespace Verso.Tests.Engine.Diarization;

public class WordBoundaryTextSplitterTests
{
    [Fact]
    public void Split_PrefersPunctuationWithinPointFourSeconds()
    {
        // 5 palavras × 1 s; "mundo." termina em 2,0 s. Troca acústica em 2,1 s.
        var (left, right) = WordBoundaryTextSplitter.Split(
            "olá mundo. como vai você",
            startSeconds: 0,
            endSeconds: 5,
            splitSeconds: 2.1);

        Assert.Equal("olá mundo.", left);
        Assert.Equal("como vai você", right);
    }

    [Fact]
    public void Split_WithoutNearbyPunctuation_CutsAtNearestWordBoundary()
    {
        var (left, right) = WordBoundaryTextSplitter.Split(
            "um dois tres quatro",
            startSeconds: 0,
            endSeconds: 4,
            splitSeconds: 2.2);

        Assert.Equal("um dois", left);
        Assert.Equal("tres quatro", right);
    }

    [Fact]
    public void Split_NeverCutsInsideAWord()
    {
        var (left, right) = WordBoundaryTextSplitter.Split(
            "abcdefghij",
            startSeconds: 0,
            endSeconds: 2,
            splitSeconds: 1.0);

        Assert.True(
            left == "abcdefghij" && right == ""
            || left == "" && right == "abcdefghij");
        Assert.DoesNotContain("abcde", new[] { left, right }.Where(s => s.Length is > 0 and < 10));
    }
}
