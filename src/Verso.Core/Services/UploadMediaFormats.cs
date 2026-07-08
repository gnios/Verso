namespace Verso.Core.Services;

public static class UploadMediaFormats
{
    public static readonly IReadOnlyList<string> Extensions =
    [
        ".mp3",
        ".wav",
        ".m4a",
        ".mp4",
        ".webm",
        ".ogg"
    ];

    public static readonly string DisplayList = "MP3, WAV, M4A, MP4, WEBM, OGG";

    public static bool IsSupported(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension)
               && Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
