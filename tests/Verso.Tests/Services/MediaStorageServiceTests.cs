namespace Verso.Tests.Services;

public class MediaStorageServiceTests
{
    [Fact]
    public async Task CopyToStorageAsync_PreservesFileNameAndExtension()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"transcriba-media-{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(tempRoot, "source");
        Directory.CreateDirectory(sourceDir);

        var sourcePath = Path.Combine(sourceDir, "entrevista.mp3");
        await File.WriteAllTextAsync(sourcePath, "audio-bytes");

        try
        {
            var transcriptionId = Guid.NewGuid();
            var service = new Verso.Core.Services.MediaStorageService(Path.Combine(tempRoot, "media"));

            var copiedPath = await service.CopyToStorageAsync(sourcePath, transcriptionId);

            Assert.Equal(
                Path.Combine(tempRoot, "media", transcriptionId.ToString("N"), "entrevista.mp3"),
                copiedPath);
            Assert.True(File.Exists(copiedPath));
            Assert.Equal("audio-bytes", await File.ReadAllTextAsync(copiedPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void DeleteMedia_RemovesTranscriptionFolder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"transcriba-media-{Guid.NewGuid():N}");
        var transcriptionId = Guid.NewGuid();
        var folder = Path.Combine(tempRoot, transcriptionId.ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "file.wav"), "data");

        try
        {
            var service = new Verso.Core.Services.MediaStorageService(tempRoot);
            service.DeleteMedia(transcriptionId);

            Assert.False(Directory.Exists(folder));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void DeleteMedia_DoesNotThrowWhenFolderMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"transcriba-media-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var service = new Verso.Core.Services.MediaStorageService(tempRoot);
            var exception = Record.Exception(() => service.DeleteMedia(Guid.NewGuid()));
            Assert.Null(exception);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
