namespace Verso.Core.Update;

public sealed record OverlayApplyResult(bool Success, string? Error)
{
    public static OverlayApplyResult Succeeded() => new(true, null);

    public static OverlayApplyResult Aborted(string error) => new(false, error);
}
