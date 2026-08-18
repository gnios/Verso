using Verso.Core.Services;

namespace Verso.Tests.Services;

public class WordLikeEditResolverTests
{
    private static WordLikeKeyContext Ctx(
        string key,
        bool shift = false,
        int start = 3,
        int end = 3,
        int length = 10,
        bool firstLine = true,
        bool lastLine = true,
        bool firstSegment = false,
        bool lastSegment = false) =>
        new(
            Key: key,
            Shift: shift,
            CaretStart: start,
            CaretEnd: end,
            TextLength: length,
            IsFirstLine: firstLine,
            IsLastLine: lastLine,
            Column: start,
            IsFirstSegment: firstSegment,
            IsLastSegment: lastSegment);

    [Fact]
    public void EnterWithoutShift_Splits()
    {
        Assert.Equal(WordLikeEditAction.Split, WordLikeEditResolver.Resolve(Ctx("Enter")));
    }

    [Fact]
    public void ShiftEnter_DoesNotSplit()
    {
        Assert.Equal(WordLikeEditAction.None, WordLikeEditResolver.Resolve(Ctx("Enter", shift: true)));
    }

    [Fact]
    public void BackspaceAtStart_MergesWithPreviousWhenNotFirstSegment()
    {
        Assert.Equal(
            WordLikeEditAction.MergePrevious,
            WordLikeEditResolver.Resolve(Ctx("Backspace", start: 0, end: 0)));
    }

    [Fact]
    public void BackspaceAtStartOfFirstSegment_DoesNothing()
    {
        Assert.Equal(
            WordLikeEditAction.None,
            WordLikeEditResolver.Resolve(Ctx("Backspace", start: 0, end: 0, firstSegment: true)));
    }

    [Fact]
    public void BackspaceWithSelection_DoesNotMerge()
    {
        Assert.Equal(
            WordLikeEditAction.None,
            WordLikeEditResolver.Resolve(Ctx("Backspace", start: 0, end: 4)));
    }

    [Fact]
    public void BackspaceInsideText_DoesNotMerge()
    {
        Assert.Equal(
            WordLikeEditAction.None,
            WordLikeEditResolver.Resolve(Ctx("Backspace", start: 3, end: 3)));
    }

    [Fact]
    public void DeleteAtEnd_MergesWithNextWhenNotLastSegment()
    {
        Assert.Equal(
            WordLikeEditAction.MergeNext,
            WordLikeEditResolver.Resolve(Ctx("Delete", start: 10, end: 10, length: 10)));
    }

    [Fact]
    public void DeleteAtEndOfLastSegment_DoesNothing()
    {
        Assert.Equal(
            WordLikeEditAction.None,
            WordLikeEditResolver.Resolve(Ctx("Delete", start: 10, end: 10, length: 10, lastSegment: true)));
    }

    [Fact]
    public void DeleteWithSelection_DoesNotMerge()
    {
        Assert.Equal(
            WordLikeEditAction.None,
            WordLikeEditResolver.Resolve(Ctx("Delete", start: 2, end: 10, length: 10)));
    }

    [Fact]
    public void ArrowLeftAtStart_MovesToPrevious()
    {
        Assert.Equal(
            WordLikeEditAction.MoveToPrevious,
            WordLikeEditResolver.Resolve(Ctx("ArrowLeft", start: 0, end: 0)));
    }

    [Fact]
    public void ArrowLeftAtStartOfFirstSegment_DoesNothing()
    {
        Assert.Equal(
            WordLikeEditAction.None,
            WordLikeEditResolver.Resolve(Ctx("ArrowLeft", start: 0, end: 0, firstSegment: true)));
    }

    [Fact]
    public void ArrowRightAtEnd_MovesToNext()
    {
        Assert.Equal(
            WordLikeEditAction.MoveToNext,
            WordLikeEditResolver.Resolve(Ctx("ArrowRight", start: 10, end: 10, length: 10)));
    }

    [Fact]
    public void ArrowRightAtEndOfLastSegment_DoesNothing()
    {
        Assert.Equal(
            WordLikeEditAction.None,
            WordLikeEditResolver.Resolve(Ctx("ArrowRight", start: 10, end: 10, length: 10, lastSegment: true)));
    }

    [Fact]
    public void ArrowUpOnFirstLine_MovesToPrevious()
    {
        Assert.Equal(
            WordLikeEditAction.MoveToPrevious,
            WordLikeEditResolver.Resolve(Ctx("ArrowUp", firstLine: true, lastLine: false)));
    }

    [Fact]
    public void ArrowUpOnFirstLineOfFirstSegment_DoesNothing()
    {
        Assert.Equal(
            WordLikeEditAction.None,
            WordLikeEditResolver.Resolve(Ctx("ArrowUp", firstLine: true, firstSegment: true)));
    }

    [Fact]
    public void ArrowUpWhenNotOnFirstLine_DoesNothing()
    {
        Assert.Equal(
            WordLikeEditAction.None,
            WordLikeEditResolver.Resolve(Ctx("ArrowUp", firstLine: false)));
    }

    [Fact]
    public void ArrowDownOnLastLine_MovesToNext()
    {
        Assert.Equal(
            WordLikeEditAction.MoveToNext,
            WordLikeEditResolver.Resolve(Ctx("ArrowDown", firstLine: false, lastLine: true)));
    }

    [Fact]
    public void ArrowDownOnLastLineOfLastSegment_DoesNothing()
    {
        Assert.Equal(
            WordLikeEditAction.None,
            WordLikeEditResolver.Resolve(Ctx("ArrowDown", lastLine: true, lastSegment: true)));
    }

    [Fact]
    public void ArrowDownWhenNotOnLastLine_DoesNothing()
    {
        Assert.Equal(
            WordLikeEditAction.None,
            WordLikeEditResolver.Resolve(Ctx("ArrowDown", lastLine: false)));
    }

    [Fact]
    public void UnrelatedKey_DoesNothing()
    {
        Assert.Equal(WordLikeEditAction.None, WordLikeEditResolver.Resolve(Ctx("a")));
        Assert.Equal(WordLikeEditAction.None, WordLikeEditResolver.Resolve(Ctx("Home")));
        Assert.Equal(WordLikeEditAction.None, WordLikeEditResolver.Resolve(Ctx("End")));
    }
}
