namespace Transcriba.Core.Services;

public class MediaStorageService
{
    private readonly string _basePath;

    public MediaStorageService(string? basePath = null)
    {
        _basePath = basePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Transcriba",
            "media");
    }

    public async Task<string> CopyToAppDataAsync(string sourcePath, Guid transcriptionId)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Arquivo de mídia não encontrado.", sourcePath);
        }

        var destinationDirectory = Path.Combine(_basePath, transcriptionId.ToString("N"));
        Directory.CreateDirectory(destinationDirectory);

        var fileName = Path.GetFileName(sourcePath);
        var destinationPath = Path.Combine(destinationDirectory, fileName);

        await using var sourceStream = File.OpenRead(sourcePath);
        await using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream);

        return destinationPath;
    }

    public void DeleteMedia(Guid transcriptionId)
    {
        var directory = Path.Combine(_basePath, transcriptionId.ToString("N"));
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
