using Verso.Core.Update;

namespace Verso.Tests.Update;

public class UpdateSessionTests
{
    [Fact]
    public void TryApplyPending_DoesNothingWhenNoStaging()
    {
        var root = CreateTempDir();
        try
        {
            var launcher = new FakeLauncher();
            var exited = false;
            var session = CreateSession(root, launcher, () => exited = true);

            Assert.False(session.TryApplyPendingAndRequestExit());
            Assert.False(launcher.Called);
            Assert.False(exited);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void TryApplyPending_LaunchesUpdaterAndRequestsExit()
    {
        var root = CreateTempDir();
        try
        {
            WritePending(root);
            var launcher = new FakeLauncher { Succeed = true };
            var exited = false;
            var session = CreateSession(root, launcher, () => exited = true);

            Assert.True(session.TryApplyPendingAndRequestExit());
            Assert.True(launcher.Called);
            Assert.True(exited);
            Assert.Equal(root, launcher.AppDir);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CheckInBackground_DoesNotApplyPendingWhenIdle()
    {
        var root = CreateTempDir();
        try
        {
            WritePending(root);
            File.WriteAllText(
                Path.Combine(root, UpdateChannel.FileName),
                """{"variant":"gpu","rid":"win-x64"}""");
            var launcher = new FakeLauncher { Succeed = true };
            var exited = false;
            var session = CreateSession(root, launcher, () => exited = true);

            await session.CheckInBackgroundAsync();

            Assert.False(launcher.Called);
            Assert.False(exited);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CheckInBackground_DoesNotCallGitHubWithoutChannel()
    {
        var root = CreateTempDir();
        try
        {
            var releases = new SilentReleases();
            var launcher = new FakeLauncher();
            var session = CreateSession(root, launcher, () => { }, releases);

            await session.CheckInBackgroundAsync();

            Assert.False(releases.Called);
            Assert.False(launcher.Called);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static UpdateSession CreateSession(
        string appDir,
        FakeLauncher launcher,
        Action exit,
        IGitHubReleaseClient? releases = null)
    {
        var coordinator = new UpdateCoordinator(
            releases ?? new SilentReleases(),
            new NoDownload(),
            new OverlayUpdateApplier(),
            new Idle(),
            () => appDir,
            () => "1.0.0");
        return new UpdateSession(
            coordinator,
            launcher,
            currentPid: () => 1,
            requestExit: exit,
            appDirectory: () => appDir,
            launchPath: () => Path.Combine(appDir, "Verso.App.exe"));
    }

    private static void WritePending(string appDir)
    {
        var payload = Path.Combine(appDir, OverlayUpdateApplier.StagingFolderName, UpdateCoordinator.PayloadFolderName);
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "Verso.App.exe"), "new");
        File.WriteAllText(
            Path.Combine(appDir, OverlayUpdateApplier.StagingFolderName, UpdateCoordinator.ReadyFileName),
            """{"tag":"v1.1.0"}""");
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "verso-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed class FakeLauncher : IUpdateProcessLauncher
    {
        public bool Succeed { get; set; } = true;
        public bool Called { get; private set; }
        public string? AppDir { get; private set; }

        public bool TryLaunch(string appDirectory, string stagingDirectory, int currentPid, string launchPath)
        {
            Called = true;
            AppDir = appDirectory;
            return Succeed;
        }
    }

    private sealed class SilentReleases : IGitHubReleaseClient
    {
        public bool Called { get; private set; }

        public Task<LatestRelease?> GetLatestAsync(string? repository = null, CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.FromResult<LatestRelease?>(null);
        }

        public GitHubAsset? FindChannelAsset(LatestRelease release, UpdateChannel channel) => null;
    }

    private sealed class NoDownload : IUpdatePackageDownloader
    {
        public Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class Idle : IUpdateIdleSignal
    {
        public bool HasActiveWork => false;
    }
}

public class UpdateStatusMessagesTests
{
    [Fact]
    public void For_DevBuildExplainsNoAutoUpdate()
    {
        var text = UpdateStatusMessages.For(UpdateStatus.Idle, hasChannel: false);
        Assert.Contains("instaladas", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("modal", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(UpdateStatus.Downloading, "Baixando")]
    [InlineData(UpdateStatus.Ready, "pronta")]
    [InlineData(UpdateStatus.Failed, "Não foi possível")]
    [InlineData(UpdateStatus.UpToDate, "atualizado")]
    public void For_ChannelPresent_DescribesStatus(UpdateStatus status, string expected)
    {
        Assert.Contains(expected, UpdateStatusMessages.For(status, hasChannel: true), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, UpdateStatus.Idle, false)]
    [InlineData(true, UpdateStatus.Idle, true)]
    [InlineData(true, UpdateStatus.UpToDate, true)]
    [InlineData(true, UpdateStatus.Failed, true)]
    [InlineData(true, UpdateStatus.Ready, true)]
    [InlineData(true, UpdateStatus.Checking, false)]
    [InlineData(true, UpdateStatus.Downloading, false)]
    [InlineData(true, UpdateStatus.Applying, false)]
    public void CanRequestUpdate_FollowsChannelAndBusyStatus(bool hasChannel, UpdateStatus status, bool expected)
    {
        Assert.Equal(expected, UpdateStatusMessages.CanRequestUpdate(hasChannel, status));
    }

    [Fact]
    public void ActionTitle_ReadyInvitesRestart()
    {
        Assert.Contains("Reiniciar", UpdateStatusMessages.ActionTitle(true, UpdateStatus.Ready));
        Assert.Contains("1.4.0", UpdateStatusMessages.ActionTitle(true, UpdateStatus.Ready, "1.4.0"));
        Assert.Contains("instaladas", UpdateStatusMessages.ActionTitle(false, UpdateStatus.Idle));
    }

    [Fact]
    public void ActionLabel_ReadyIncludesTargetVersion()
    {
        Assert.Equal("Atualizar para 1.4.0", UpdateStatusMessages.ActionLabel(true, UpdateStatus.Ready, "1.4.0"));
        Assert.Equal("Atualizar agora", UpdateStatusMessages.ActionLabel(true, UpdateStatus.Ready, "1.1.0", "1.4.0"));
        Assert.Equal("Baixando 1.4.0…", UpdateStatusMessages.ActionLabel(true, UpdateStatus.Downloading, "1.4.0"));
        Assert.Equal("Verificar atualizações", UpdateStatusMessages.ActionLabel(true, UpdateStatus.Idle));
        Assert.Equal("Atualizar", UpdateStatusMessages.ActionLabel(false, UpdateStatus.Idle));
    }

    [Fact]
    public void For_ReadyIncludesTargetVersion()
    {
        var text = UpdateStatusMessages.For(UpdateStatus.Ready, hasChannel: true, availableVersion: "1.4.0");
        Assert.Contains("1.4.0", text);
        Assert.Contains("pronta", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RestartConfirm_WarnsThatAppMustClose()
    {
        var text = UpdateStatusMessages.RestartConfirm("1.4.0");
        Assert.Contains("fechar", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.4.0", text);
        Assert.Contains("Reiniciar", UpdateStatusMessages.RestartConfirmTitle);
    }
}
