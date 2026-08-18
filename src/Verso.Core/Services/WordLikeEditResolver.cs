namespace Verso.Core.Services;

public enum WordLikeEditAction
{
    None,
    Split,
    MergePrevious,
    MergeNext,
    MoveToPrevious,
    MoveToNext,
}

public readonly record struct WordLikeKeyContext(
    string Key,
    bool Shift,
    int CaretStart,
    int CaretEnd,
    int TextLength,
    bool IsFirstLine,
    bool IsLastLine,
    int Column,
    bool IsFirstSegment,
    bool IsLastSegment);

public static class WordLikeEditResolver
{
    public static WordLikeEditAction Resolve(WordLikeKeyContext ctx)
    {
        var collapsed = ctx.CaretStart == ctx.CaretEnd;

        return ctx.Key switch
        {
            "Enter" when !ctx.Shift => WordLikeEditAction.Split,
            "Backspace" when collapsed && ctx.CaretStart == 0 && !ctx.IsFirstSegment
                => WordLikeEditAction.MergePrevious,
            "Delete" when collapsed && ctx.CaretStart == ctx.TextLength && !ctx.IsLastSegment
                => WordLikeEditAction.MergeNext,
            "ArrowLeft" when collapsed && ctx.CaretStart == 0 && !ctx.IsFirstSegment
                => WordLikeEditAction.MoveToPrevious,
            "ArrowRight" when collapsed && ctx.CaretStart == ctx.TextLength && !ctx.IsLastSegment
                => WordLikeEditAction.MoveToNext,
            "ArrowUp" when ctx.IsFirstLine && !ctx.IsFirstSegment
                => WordLikeEditAction.MoveToPrevious,
            "ArrowDown" when ctx.IsLastLine && !ctx.IsLastSegment
                => WordLikeEditAction.MoveToNext,
            _ => WordLikeEditAction.None,
        };
    }
}
