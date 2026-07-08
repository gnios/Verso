using Verso.Core.Engine;

namespace Verso.Tests.Engine;

public class FfmpegLocatorTests
{
    [Fact]
    public void EnsureFfmpeg_WhenPathContainsFfmpeg_ReturnsPath()
    {
        var ffmpegDir = @"C:\tools\ffmpeg\bin";
        var expectedPath = Path.Combine(ffmpegDir, "ffmpeg.exe");

        var locator = new FfmpegLocator(
            () => [ffmpegDir],
            path => path == expectedPath,
            _ => false,
            _ => [],
            () => false,
            isWindows: true);

        var result = locator.EnsureFfmpeg();

        Assert.Equal(expectedPath, result);
    }

    [Fact]
    public void EnsureFfmpeg_WhenNotFound_ThrowsFfmpegNotFoundException()
    {
        var locator = new FfmpegLocator(
            () => [@"C:\empty\bin"],
            _ => false,
            _ => false,
            _ => [],
            () => false,
            isWindows: true);

        var ex = Assert.Throws<FfmpegNotFoundException>(() => locator.EnsureFfmpeg());

        Assert.Contains("ffmpeg não encontrado", ex.Message);
    }
}
