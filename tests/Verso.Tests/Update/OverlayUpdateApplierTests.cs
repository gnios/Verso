using Verso.Core.Update;

namespace Verso.Tests.Update;

public class OverlayUpdateApplierTests
{
    [Fact]
    public void Apply_ReplacesBinariesAndPreservesDataAndExtras()
    {
        var root = CreateTempDir();
        try
        {
            var app = Path.Combine(root, "app");
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(Path.Combine(app, "data", "models"));
            Directory.CreateDirectory(Path.Combine(app, "engine"));
            File.WriteAllText(Path.Combine(app, "Verso.App.exe"), "old-exe");
            File.WriteAllText(Path.Combine(app, "engine", "Verso.Worker.exe"), "old-worker");
            File.WriteAllText(Path.Combine(app, "data", "verso.db"), "db");
            File.WriteAllText(Path.Combine(app, "data", "models", "x.bin"), "model");
            File.WriteAllText(Path.Combine(app, "notas.txt"), "keep-me");

            Directory.CreateDirectory(Path.Combine(staging, "engine"));
            Directory.CreateDirectory(Path.Combine(staging, "data"));
            File.WriteAllText(Path.Combine(staging, "Verso.App.exe"), "new-exe");
            File.WriteAllText(Path.Combine(staging, "engine", "Verso.Worker.exe"), "new-worker");
            File.WriteAllText(Path.Combine(staging, "data", "verso.db"), "SHOULD-NOT-COPY");

            var result = new OverlayUpdateApplier().Apply(staging, app);

            Assert.True(result.Success);
            Assert.Equal("new-exe", File.ReadAllText(Path.Combine(app, "Verso.App.exe")));
            Assert.Equal("new-worker", File.ReadAllText(Path.Combine(app, "engine", "Verso.Worker.exe")));
            Assert.Equal("db", File.ReadAllText(Path.Combine(app, "data", "verso.db")));
            Assert.Equal("model", File.ReadAllText(Path.Combine(app, "data", "models", "x.bin")));
            Assert.Equal("keep-me", File.ReadAllText(Path.Combine(app, "notas.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Apply_AbortsWhenStagingHasNoAppHost()
    {
        var root = CreateTempDir();
        try
        {
            var app = Path.Combine(root, "app");
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(app);
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(app, "Verso.App.exe"), "old-exe");
            File.WriteAllText(Path.Combine(app, "keep.txt"), "x");
            File.WriteAllText(Path.Combine(staging, "readme.txt"), "no host");

            var result = new OverlayUpdateApplier().Apply(staging, app);

            Assert.False(result.Success);
            Assert.Equal("old-exe", File.ReadAllText(Path.Combine(app, "Verso.App.exe")));
            Assert.True(File.Exists(Path.Combine(app, "keep.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Apply_RemovesUpdateStagingInsideAppDirectory()
    {
        var root = CreateTempDir();
        try
        {
            var app = Path.Combine(root, "app");
            var stagingRoot = Path.Combine(app, OverlayUpdateApplier.StagingFolderName);
            var payload = Path.Combine(stagingRoot, UpdateCoordinator.PayloadFolderName);
            Directory.CreateDirectory(app);
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(app, "Verso.App.exe"), "old-exe");
            File.WriteAllText(Path.Combine(payload, "Verso.App.exe"), "new-exe");
            File.WriteAllText(Path.Combine(stagingRoot, UpdateCoordinator.ReadyFileName), """{"tag":"v1.4.0"}""");

            var result = new OverlayUpdateApplier().Apply(payload, app);

            Assert.True(result.Success);
            Assert.Equal("new-exe", File.ReadAllText(Path.Combine(app, "Verso.App.exe")));
            Assert.False(Directory.Exists(stagingRoot));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Apply_AcceptsUnixAppHostName()
    {
        var root = CreateTempDir();
        try
        {
            var app = Path.Combine(root, "app");
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(app);
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "Verso.App"), "unix-host");

            var result = new OverlayUpdateApplier().Apply(staging, app);

            Assert.True(result.Success);
            Assert.Equal("unix-host", File.ReadAllText(Path.Combine(app, "Verso.App")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "verso-overlay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
