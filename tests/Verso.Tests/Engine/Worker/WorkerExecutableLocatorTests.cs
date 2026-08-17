using Verso.Core.Engine.Worker;

namespace Verso.Tests.Engine.Worker;

public class WorkerExecutableLocatorTests
{
    [Fact]
    public void Resolve_WhenExecutableExists_ReturnsPath()
    {
        var appDir = Path.Combine(Path.GetTempPath(), "verso-locator-test");
        var fileName = "Verso.Worker.exe";
        var expectedPath = Path.Combine(appDir, fileName);

        var locator = new WorkerExecutableLocator(
            () => appDir,
            path => path == expectedPath,
            fileName);

        var result = locator.Resolve();

        Assert.Equal(expectedPath, result);
    }

    [Fact]
    public void Resolve_WhenUnixWorkerExists_ReturnsPathWithoutExe()
    {
        var appDir = Path.Combine(Path.GetTempPath(), "verso-locator-unix");
        var fileName = "Verso.Worker";
        var expectedPath = Path.Combine(appDir, fileName);

        var locator = new WorkerExecutableLocator(
            () => appDir,
            path => path == expectedPath,
            fileName);

        Assert.Equal(expectedPath, locator.Resolve());
    }

    [Fact]
    public void Resolve_WhenExecutableMissing_ThrowsFileNotFoundException()
    {
        var fileName = WorkerExecutableLocator.WorkerFileName;
        var locator = new WorkerExecutableLocator(
            () => Path.Combine(Path.GetTempPath(), "verso-missing"),
            _ => false,
            fileName);

        var ex = Assert.Throws<FileNotFoundException>(() => locator.Resolve());

        Assert.Contains(fileName, ex.Message);
    }
}
