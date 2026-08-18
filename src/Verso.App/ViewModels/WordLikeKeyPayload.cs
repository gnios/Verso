using Verso.Core.Services;

namespace Verso.App.ViewModels;

public sealed class WordLikeKeyPayload
{
    public string Key { get; set; } = "";
    public bool Shift { get; set; }
    public int CaretStart { get; set; }
    public int CaretEnd { get; set; }
    public int TextLength { get; set; }
    public bool IsFirstLine { get; set; }
    public bool IsLastLine { get; set; }
    public int Column { get; set; }

    public WordLikeKeyContext ToContext() =>
        new(
            Key,
            Shift,
            CaretStart,
            CaretEnd,
            TextLength,
            IsFirstLine,
            IsLastLine,
            Column,
            IsFirstSegment: false,
            IsLastSegment: false);
}
