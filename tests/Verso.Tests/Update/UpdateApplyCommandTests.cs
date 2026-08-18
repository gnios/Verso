using Verso.Core.Update;

namespace Verso.Tests.Update;

public class UpdateApplyCommandTests
{
    [Fact]
    public void TryParse_ReadsCliFlags()
    {
        var cmd = UpdateApplyCommand.TryParse(
        [
            "--pid", "42",
            "--app-dir", @"C:\Verso",
            "--staging", @"C:\Verso\update-staging\payload",
            "--launch", @"C:\Verso\Verso.App.exe"
        ]);

        Assert.NotNull(cmd);
        Assert.Equal(42, cmd.Pid);
        Assert.Equal(@"C:\Verso", cmd.AppDirectory);
        Assert.Equal(@"C:\Verso\update-staging\payload", cmd.StagingDirectory);
        Assert.Equal(@"C:\Verso\Verso.App.exe", cmd.LaunchPath);
    }

    [Fact]
    public void TryParse_ReturnsNullWhenIncomplete()
    {
        Assert.Null(UpdateApplyCommand.TryParse(["--pid", "1"]));
    }

    [Fact]
    public void Execute_WaitsForPidThenAppliesAndLaunches()
    {
        var root = Path.Combine(Path.GetTempPath(), "verso-apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var app = Path.Combine(root, "app");
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(app);
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(app, "Verso.App.exe"), "old");
            File.WriteAllText(Path.Combine(app, "keep.txt"), "extra");
            File.WriteAllText(Path.Combine(staging, "Verso.App.exe"), "new");

            var running = true;
            string? launched = null;
            var cmd = new UpdateApplyCommand
            {
                Pid = 9,
                AppDirectory = app,
                StagingDirectory = staging,
                LaunchPath = Path.Combine(app, "Verso.App.exe")
            };

            var thread = new Thread(() =>
            {
                Thread.Sleep(80);
                running = false;
            });
            thread.Start();

            var exit = cmd.Execute(
                new OverlayUpdateApplier(),
                _ => running,
                (launch, dir) =>
                {
                    launched = launch;
                    Assert.Equal(app, dir);
                    return true;
                },
                pollMilliseconds: 10,
                timeoutMilliseconds: 5_000,
                settleMilliseconds: 0);

            thread.Join();
            Assert.Equal(0, exit);
            Assert.Equal("new", File.ReadAllText(Path.Combine(app, "Verso.App.exe")));
            Assert.Equal("extra", File.ReadAllText(Path.Combine(app, "keep.txt")));
            Assert.Equal(cmd.LaunchPath, launched);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Execute_Returns3WhenPidNeverExits()
    {
        var cmd = new UpdateApplyCommand
        {
            Pid = 1,
            AppDirectory = ".",
            StagingDirectory = ".",
            LaunchPath = "x"
        };

        var exit = cmd.Execute(
            new OverlayUpdateApplier(),
            _ => true,
            (_, _) => true,
            pollMilliseconds: 5,
            timeoutMilliseconds: 20);

        Assert.Equal(3, exit);
    }

    [Fact]
    public void Execute_LaunchesAppEvenWhenApplyFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "verso-apply-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var app = Path.Combine(root, "app");
            Directory.CreateDirectory(app);
            File.WriteAllText(Path.Combine(app, "Verso.App.exe"), "old");
            var launched = false;
            var cmd = new UpdateApplyCommand
            {
                Pid = 1,
                AppDirectory = app,
                StagingDirectory = Path.Combine(root, "missing"),
                LaunchPath = Path.Combine(app, "Verso.App.exe")
            };

            var exit = cmd.Execute(
                new OverlayUpdateApplier(),
                _ => false,
                (_, _) =>
                {
                    launched = true;
                    return true;
                },
                pollMilliseconds: 5,
                timeoutMilliseconds: 20,
                settleMilliseconds: 0);

            Assert.Equal(1, exit);
            Assert.True(launched);
            Assert.Equal("old", File.ReadAllText(Path.Combine(app, "Verso.App.exe")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
