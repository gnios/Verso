namespace Verso.Core.Update;

public sealed record GitHubAsset(string Name, string BrowserDownloadUrl, long Size);

public sealed record LatestRelease(string TagName, IReadOnlyList<GitHubAsset> Assets);

public interface IGitHubReleaseClient
{
    Task<LatestRelease?> GetLatestAsync(string? repository = null, CancellationToken cancellationToken = default);

    GitHubAsset? FindChannelAsset(LatestRelease release, UpdateChannel channel);
}
