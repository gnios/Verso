using Verso.Core.Engine.Worker;

namespace Verso.Tests.Engine.Worker;

public class WorkerExecutableLocatorTests
{
    [Fact]
    public void Resolve_WhenExecutableExists_ReturnsPath()
    {
        var appDir = @"C:\apps\Verso";
        var expectedPath = Path.Combine(appDir, "Verso.Worker.exe");

        var locator = new WorkerExecutableLocator(
            () => appDir,
            path => path == expectedPath);

        var result = locator.Resolve();

        Assert.Equal(expectedPath, result);
    }

    [Fact]
    public void Resolve_WhenExecutableMissing_ThrowsFileNotFoundException()
    {
        var locator = new WorkerExecutableLocator(
            () => @"C:\apps\Verso",
            _ => false);

        var ex = Assert.Throws<FileNotFoundException>(() => locator.Resolve());

        Assert.Contains("Verso.Worker.exe", ex.Message);
    }
}
