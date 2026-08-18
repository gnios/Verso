using System.Net;
using System.Net.Http;
using Verso.Core.Update;

namespace Verso.Tests.Update;

public class GitHubReleaseClientTests
{
    [Fact]
    public void ResolveRepository_DefaultsToGniosVerso()
    {
        var previous = Environment.GetEnvironmentVariable(GitHubReleaseClient.RepositoryEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(GitHubReleaseClient.RepositoryEnvironmentVariable, null);
            Assert.Equal("gnios/Verso", GitHubReleaseClient.ResolveRepository(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable(GitHubReleaseClient.RepositoryEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ResolveRepository_PrefersArgumentThenEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable(GitHubReleaseClient.RepositoryEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(GitHubReleaseClient.RepositoryEnvironmentVariable, "env/Repo");
            Assert.Equal("arg/Repo", GitHubReleaseClient.ResolveRepository("arg/Repo"));
            Assert.Equal("env/Repo", GitHubReleaseClient.ResolveRepository(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable(GitHubReleaseClient.RepositoryEnvironmentVariable, previous);
        }
    }

    [Fact]
    public async Task GetLatestAsync_ParsesTagAndAssets()
    {
        var handler = new StubHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"tag_name":"v1.2.4","assets":[
                      {"name":"Verso-1.2.4-gpu-win-x64.zip","browser_download_url":"https://example.test/gpu.zip","size":10},
                      {"name":"Verso-1.2.4-cpu-win-x64.zip","browser_download_url":"https://example.test/cpu.zip","size":5}
                    ]}
                    """)
            }
        };
        var client = new GitHubReleaseClient(new HttpClient(handler), "https://api.test");

        var latest = await client.GetLatestAsync("gnios/Verso");

        Assert.NotNull(latest);
        Assert.Equal("v1.2.4", latest.TagName);
        Assert.Equal(2, latest.Assets.Count);
        Assert.Equal("https://api.test/repos/gnios/Verso/releases/latest", handler.LastRequest?.RequestUri?.ToString());
        Assert.Contains("Verso", handler.LastRequest!.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsNullOnHttpFailure()
    {
        var handler = new StubHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        };
        var client = new GitHubReleaseClient(new HttpClient(handler), "https://api.test");

        Assert.Null(await client.GetLatestAsync("gnios/Verso"));
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsNullOnNetworkError()
    {
        var handler = new StubHandler { Throw = new HttpRequestException("offline") };
        var client = new GitHubReleaseClient(new HttpClient(handler), "https://api.test");

        Assert.Null(await client.GetLatestAsync("gnios/Verso"));
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsNullOnInvalidRepository()
    {
        var handler = new StubHandler();
        var client = new GitHubReleaseClient(new HttpClient(handler), "https://api.test");

        Assert.Null(await client.GetLatestAsync("not-a-repo"));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public void FindChannelAsset_MatchesVariantAndRid()
    {
        var client = new GitHubReleaseClient(new HttpClient());
        var release = new LatestRelease("v1.2.4",
        [
            new GitHubAsset("Verso-1.2.4-cpu-win-x64.zip", "https://example.test/cpu.zip", 1),
            new GitHubAsset("Verso-1.2.4-gpu-win-x64.zip", "https://example.test/gpu.zip", 2),
        ]);

        var gpu = client.FindChannelAsset(release, new UpdateChannel("gpu", "win-x64"));
        Assert.Equal("https://example.test/gpu.zip", gpu?.BrowserDownloadUrl);

        Assert.Null(client.FindChannelAsset(release, new UpdateChannel("gpu", "linux-x64")));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);
        public Exception? Throw { get; set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (Throw is not null)
                throw Throw;
            return Task.FromResult(Response);
        }
    }
}
