using Verso.Core.Engine.Diarization;

namespace Verso.Tests.Engine.Diarization;

public class WordBoundaryTextSplitterTests
{
    [Fact]
    public void Split_PrefersPunctuationWithinPointFourSeconds()
    {
        // 8 palavras em 2 s (0,25 s cada). "c." cai em 0,75 s; a troca em 0,95 s
        // fica a 0,20 s da pontuação e a 0,05 s da fronteira seguinte ("d").
        // Sem a janela ±0,4 s o corte iria para depois de "d".
        var (left, right) = WordBoundaryTextSplitter.Split(
            "a b c. d e f g h",
            startSeconds: 0,
            endSeconds: 2,
            splitSeconds: 0.95);

        Assert.Equal("a b c.", left);
        Assert.Equal("d e f g h", right);
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
