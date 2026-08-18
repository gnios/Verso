namespace Verso.Core.Update;

public interface IUpdateIdleSignal
{
    bool HasActiveWork { get; }
}

public interface IUpdatePackageDownloader
{
    Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken = default);
}

public enum UpdateStatus
{
    Idle,
    Checking,
    Downloading,
    Ready,
    Applying,
    UpToDate,
    Failed
}

public sealed record UpdateCheckResult(
    UpdateStatus Status,
    bool ApplyImmediately,
    string? Detail);
