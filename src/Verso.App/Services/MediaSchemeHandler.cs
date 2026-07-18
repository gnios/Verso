using System;
using System.IO;
using Verso.Core;

namespace Verso.App.Services;

/// <summary>
/// Validação/MIME de paths sob <see cref="VersoPaths.MediaDirectory"/> e helpers do
/// antigo scheme <c>versomedia://</c>. O playback real usa <see cref="LocalMediaServer"/>
/// (HTTP + Range): o Photino custom-scheme materializa o Stream inteiro em memória.
/// </summary>
public static class MediaSchemeHandler
{
    public const string Scheme = "versomedia";

    public static string BuildUrl(string absoluteFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);
        var escaped = Uri.EscapeDataString(Path.GetFullPath(absoluteFilePath));
        return $"{Scheme}://local/?path={escaped}";
    }

    /// <summary>
    /// Handler legado para <c>PhotinoWindow.RegisterCustomSchemeHandler</c> (não usar para áudio grande).
    /// Retorna <c>null</c> (e contentType vazio) se o path for inválido ou estiver fora de media/.
    /// </summary>
    public static Stream? Handle(object sender, string scheme, string url, out string contentType)
    {
        contentType = string.Empty;

        if (!TryResolveMediaPath(url, out var filePath))
        {
            return null;
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        contentType = GetContentType(filePath);
        return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    /// <summary>
    /// Resolve e valida o path absoluto embutido na URL do scheme.
    /// Expõe a lógica para testes unitários sem abrir o arquivo.
    /// </summary>
    public static bool TryResolveMediaPath(string url, out string filePath)
    {
        filePath = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawPath = GetQueryValue(uri.Query, "path");
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath);
        }
        catch
        {
            return false;
        }

        if (!IsUnderMediaDirectory(fullPath))
        {
            return false;
        }

        filePath = fullPath;
        return true;
    }

    public static bool IsUnderMediaDirectory(string fullPath)
    {
        var mediaRoot = Path.GetFullPath(VersoPaths.MediaDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var candidate = Path.GetFullPath(fullPath);
        return candidate.StartsWith(mediaRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetContentType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".mp4" => "audio/mp4",
            ".webm" => "audio/webm",
            ".ogg" => "audio/ogg",
            _ => "application/octet-stream",
        };

    private static string? GetQueryValue(string query, string key)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        var trimmed = query[0] == '?' ? query[1..] : query;
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(part[..eq]);
            if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.UnescapeDataString(part[(eq + 1)..]);
        }

        return null;
    }
}
