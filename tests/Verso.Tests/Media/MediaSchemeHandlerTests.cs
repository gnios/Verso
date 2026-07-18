using Verso.App.Services;
using Verso.Core;

namespace Verso.Tests.Media;

public class MediaSchemeHandlerTests
{
    [Fact]
    public void TryResolveMediaPath_AcceptsPathUnderMediaDirectory()
    {
        var mediaRoot = Path.GetFullPath(VersoPaths.MediaDirectory);
        Directory.CreateDirectory(mediaRoot);
        var filePath = Path.Combine(mediaRoot, "abc123", "sample.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var url = MediaSchemeHandler.BuildUrl(filePath);

        Assert.True(MediaSchemeHandler.TryResolveMediaPath(url, out var resolved));
        Assert.Equal(Path.GetFullPath(filePath), resolved);
    }

    [Fact]
    public void TryResolveMediaPath_RejectsPathOutsideMediaDirectory()
    {
        var outside = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"verso-outside-{Guid.NewGuid():N}.mp3"));
        var url = $"versomedia://local/?path={Uri.EscapeDataString(outside)}";

        Assert.False(MediaSchemeHandler.TryResolveMediaPath(url, out _));
    }

    [Fact]
    public void TryResolveMediaPath_RejectsTraversalOutsideMediaDirectory()
    {
        var mediaRoot = Path.GetFullPath(VersoPaths.MediaDirectory);
        var traversal = Path.Combine(mediaRoot, "..", "secrets.txt");
        var url = $"versomedia://local/?path={Uri.EscapeDataString(traversal)}";

        Assert.False(MediaSchemeHandler.TryResolveMediaPath(url, out _));
    }

    [Fact]
    public void GetContentType_MapsKnownExtensions()
    {
        Assert.Equal("audio/mpeg", MediaSchemeHandler.GetContentType("a.mp3"));
        Assert.Equal("audio/wav", MediaSchemeHandler.GetContentType("a.wav"));
        Assert.Equal("audio/mp4", MediaSchemeHandler.GetContentType("a.m4a"));
        Assert.Equal("audio/ogg", MediaSchemeHandler.GetContentType("a.ogg"));
        Assert.Equal("application/octet-stream", MediaSchemeHandler.GetContentType("a.bin"));
    }

    [Fact]
    public void Handle_ReturnsStream_ForExistingMediaFile()
    {
        var mediaRoot = Path.GetFullPath(VersoPaths.MediaDirectory);
        var filePath = Path.Combine(mediaRoot, Guid.NewGuid().ToString("N"), "clip.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllBytes(filePath, [0x52, 0x49, 0x46, 0x46]);

        try
        {
            var url = MediaSchemeHandler.BuildUrl(filePath);
            using var stream = MediaSchemeHandler.Handle(new object(), MediaSchemeHandler.Scheme, url, out var contentType);

            Assert.NotNull(stream);
            Assert.Equal("audio/wav", contentType);
            Assert.True(stream!.Length >= 4);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            var dir = Path.GetDirectoryName(filePath);
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
