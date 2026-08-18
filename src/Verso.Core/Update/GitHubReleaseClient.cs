using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Verso.Core.Update;

public sealed class GitHubReleaseClient : IGitHubReleaseClient
{
    public const string DefaultRepository = "gnios/Verso";
    public const string RepositoryEnvironmentVariable = "VERSO_UPDATE_REPO";

    private readonly HttpClient _http;
    private readonly string _apiRoot;

    public GitHubReleaseClient(HttpClient http, string? apiRoot = null)
    {
        _http = http;
        _apiRoot = string.IsNullOrWhiteSpace(apiRoot)
            ? "https://api.github.com"
            : apiRoot.TrimEnd('/');

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Verso");
        if (_http.DefaultRequestHeaders.Accept.Count == 0)
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public static string ResolveRepository(string? repository = null)
    {
        if (!string.IsNullOrWhiteSpace(repository))
            return repository.Trim();

        var fromEnv = Environment.GetEnvironmentVariable(RepositoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();

        return DefaultRepository;
    }

    public async Task<LatestRelease?> GetLatestAsync(
        string? repository = null,
        CancellationToken cancellationToken = default)
    {
        var repo = ResolveRepository(repository);
        if (repo.Count(c => c == '/') != 1)
            return null;

        try
        {
            using var response = await _http
                .GetAsync($"{_apiRoot}/repos/{repo}/releases/latest", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize(json, GitHubReleaseJsonContext.Default.GitHubReleaseDto);
            if (dto is null || string.IsNullOrWhiteSpace(dto.TagName))
                return null;

            var assets = (dto.Assets ?? [])
                .Select(a => new GitHubAsset(
                    a.Name ?? "",
                    a.BrowserDownloadUrl ?? "",
                    a.Size))
                .Where(a => a.Name.Length > 0 && a.BrowserDownloadUrl.Length > 0)
                .ToArray();

            return new LatestRelease(dto.TagName, assets);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public GitHubAsset? FindChannelAsset(LatestRelease release, UpdateChannel channel)
    {
        var version = AppVersion.Parse(release.TagName);
        var name = channel.AssetFileName($"{version.Major}.{version.Minor}.{version.Build}");
        return release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed record GitHubReleaseDto(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAssetDto>? Assets);

internal sealed record GitHubAssetDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl,
    [property: JsonPropertyName("size")] long Size);

[JsonSerializable(typeof(GitHubReleaseDto))]
[JsonSerializable(typeof(GitHubAssetDto))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class GitHubReleaseJsonContext : JsonSerializerContext;
