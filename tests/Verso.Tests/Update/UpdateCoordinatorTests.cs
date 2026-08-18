using System.IO.Compression;
using System.Text;
using Verso.Core.Update;

namespace Verso.Tests.Update;

public class UpdateCoordinatorTests
{
    [Fact]
    public async Task CheckAndPrepare_SkipsGitHubWhenChannelMissing()
    {
        var root = CreateTempDir();
        try
        {
            var releases = new FakeReleases();
            var coordinator = CreateCoordinator(root, releases, idle: false);

            var result = await coordinator.CheckAndPrepareAsync();

            Assert.Equal(UpdateStatus.Idle, result.Status);
            Assert.False(releases.Called);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CheckAndPrepare_DoesNotDownloadWhenNotNewer()
    {
        var root = CreateTempDir();
        try
        {
            WriteChannel(root);
            var releases = new FakeReleases
            {
                Latest = new LatestRelease("v1.0.0",
                [
                    new GitHubAsset("Verso-1.0.0-gpu-win-x64.zip", "https://example.test/a.zip", 1)
                ])
            };
            var downloader = new FakeDownloader();
            var coordinator = CreateCoordinator(root, releases, idle: true, downloader, localVersion: "1.0.0");

            var result = await coordinator.CheckAndPrepareAsync();

            Assert.Equal(UpdateStatus.UpToDate, result.Status);
            Assert.False(downloader.Called);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CheckAndPrepare_DownloadsAndStagesWhenNewerAndIdle()
    {
        var root = CreateTempDir();
        try
        {
            WriteChannel(root);
            var zip = CreatePackageZip(root, includeData: true);
            var releases = NewerRelease("https://example.test/pkg.zip");
            var downloader = new FakeDownloader { SourceZip = zip };
            var coordinator = CreateCoordinator(root, releases, idle: true, downloader, localVersion: "1.0.0");

            var result = await coordinator.CheckAndPrepareAsync();

            Assert.Equal(UpdateStatus.Ready, result.Status);
            Assert.True(result.ApplyImmediately);
            Assert.True(coordinator.HasPendingApply());
            Assert.True(File.Exists(Path.Combine(coordinator.PayloadDirectory, "Verso.App.exe")));
            Assert.False(Directory.Exists(Path.Combine(coordinator.PayloadDirectory, "data")));
            Assert.False(File.Exists(Path.Combine(coordinator.StagingDirectory, UpdateCoordinator.PackageFileName)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CheckAndPrepare_DoesNotApplyImmediatelyWhenBusy()
    {
        var root = CreateTempDir();
        try
        {
            WriteChannel(root);
            var zip = CreatePackageZip(root, includeData: false);
            var coordinator = CreateCoordinator(
                root,
                NewerRelease("https://example.test/pkg.zip"),
                idle: false,
                new FakeDownloader { SourceZip = zip },
                localVersion: "1.0.0");

            var result = await coordinator.CheckAndPrepareAsync();

            Assert.Equal(UpdateStatus.Ready, result.Status);
            Assert.False(result.ApplyImmediately);
            Assert.True(coordinator.HasPendingApply());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CheckAndPrepare_DiscardsPartialDownloadOnFailure()
    {
        var root = CreateTempDir();
        try
        {
            WriteChannel(root);
            var downloader = new FakeDownloader { Throw = new IOException("cut") };
            var coordinator = CreateCoordinator(
                root,
                NewerRelease("https://example.test/pkg.zip"),
                idle: true,
                downloader,
                localVersion: "1.0.0");

            var result = await coordinator.CheckAndPrepareAsync();

            Assert.Equal(UpdateStatus.Failed, result.Status);
            Assert.False(File.Exists(Path.Combine(coordinator.StagingDirectory, UpdateCoordinator.PackageFileName)));
            Assert.False(coordinator.HasPendingApply());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CheckAndPrepare_SecondInstanceSkipsWhenLocked()
    {
        var root = CreateTempDir();
        try
        {
            WriteChannel(root);
            var lockPath = Path.Combine(root, UpdateCoordinator.LockFileName);
            using var held = new FileStream(lockPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            var releases = new FakeReleases();
            var coordinator = CreateCoordinator(root, releases, idle: true);

            var result = await coordinator.CheckAndPrepareAsync();

            Assert.Equal("lock", result.Detail);
            Assert.False(releases.Called);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CheckAndPrepare_FailsWhenChannelAssetMissing()
    {
        var root = CreateTempDir();
        try
        {
            WriteChannel(root);
            var releases = new FakeReleases
            {
                Latest = new LatestRelease("v2.0.0",
                [
                    new GitHubAsset("Verso-2.0.0-cpu-linux-x64.zip", "https://example.test/a.zip", 1)
                ])
            };
            var downloader = new FakeDownloader();
            var coordinator = CreateCoordinator(root, releases, idle: true, downloader, localVersion: "1.0.0");

            var result = await coordinator.CheckAndPrepareAsync();

            Assert.Equal(UpdateStatus.Failed, result.Status);
            Assert.False(downloader.Called);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CheckAndPrepare_ReturnsFailedWhenLatestQueryFails()
    {
        var root = CreateTempDir();
        try
        {
            WriteChannel(root);
            var coordinator = CreateCoordinator(root, new FakeReleases { Latest = null }, idle: true);

            var result = await coordinator.CheckAndPrepareAsync();

            Assert.Equal(UpdateStatus.Failed, result.Status);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static UpdateCoordinator CreateCoordinator(
        string appDir,
        FakeReleases releases,
        bool idle,
        FakeDownloader? downloader = null,
        string localVersion = "1.0.0") =>
        new(
            releases,
            downloader ?? new FakeDownloader(),
            new OverlayUpdateApplier(),
            new FakeIdle(!idle),
            () => appDir,
            () => localVersion);

    private static FakeReleases NewerRelease(string url) => new()
    {
        Latest = new LatestRelease("v1.1.0",
        [
            new GitHubAsset("Verso-1.1.0-gpu-win-x64.zip", url, 10)
        ])
    };

    private static void WriteChannel(string dir) =>
        File.WriteAllText(
            Path.Combine(dir, UpdateChannel.FileName),
            """{"variant":"gpu","rid":"win-x64"}""");

    private static string CreatePackageZip(string root, bool includeData)
    {
        var src = Path.Combine(root, "pkg-src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "Verso.App.exe"), "new");
        if (includeData)
        {
            Directory.CreateDirectory(Path.Combine(src, "data"));
            File.WriteAllText(Path.Combine(src, "data", "verso.db"), "nope");
        }

        var zip = Path.Combine(root, "pkg.zip");
        if (File.Exists(zip))
            File.Delete(zip);
        ZipFile.CreateFromDirectory(src, zip);
        return zip;
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "verso-coord-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed class FakeReleases : IGitHubReleaseClient
    {
        public LatestRelease? Latest { get; set; }
        public bool Called { get; private set; }

        public Task<LatestRelease?> GetLatestAsync(
            string? repository = null,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.FromResult(Latest);
        }

        public GitHubAsset? FindChannelAsset(LatestRelease release, UpdateChannel channel) =>
            new GitHubReleaseClient(new HttpClient()).FindChannelAsset(release, channel);
    }

    private sealed class FakeDownloader : IUpdatePackageDownloader
    {
        public string? SourceZip { get; set; }
        public Exception? Throw { get; set; }
        public bool Called { get; private set; }

        public Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken = default)
        {
            Called = true;
            if (Throw is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.WriteAllBytes(destinationPath, Encoding.UTF8.GetBytes("partial"));
                throw Throw;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(SourceZip!, destinationPath, overwrite: true);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIdle(bool hasWork) : IUpdateIdleSignal
    {
        public bool HasActiveWork { get; } = hasWork;
    }
}
