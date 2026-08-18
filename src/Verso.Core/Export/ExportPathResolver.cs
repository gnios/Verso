namespace Verso.Core.Export;

public static class ExportPathResolver
{
    public static string GetDownloadsDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = Path.Combine(home, "Downloads");
        if (Directory.Exists(downloads))
        {
            return downloads;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrWhiteSpace(documents) ? home : documents;
    }

    public static string CreateUniquePath(string directory, string? suggestedFileName, string extension)
    {
        Directory.CreateDirectory(directory);
        var ext = extension.TrimStart('.');
        var name = TranscriptionTextFormatter.SanitizeFileName(suggestedFileName);
        var path = Path.Combine(directory, $"{name}.{ext}");
        for (var i = 1; File.Exists(path); i++)
        {
            path = Path.Combine(directory, $"{name} ({i}).{ext}");
        }

        return path;
    }

    public static string CreateUniqueDownloadPath(string? suggestedFileName, string extension) =>
        CreateUniquePath(GetDownloadsDirectory(), suggestedFileName, extension);
}
