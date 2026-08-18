using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Verso.Core.Export;

namespace Verso.App.Services;

public sealed class PhotinoFileSaveService : IFileSaveService
{
    public Task<string?> PickSavePathAsync(string suggestedFileName, ExportFormat format)
    {
        var extension = format switch
        {
            ExportFormat.Txt => "txt",
            ExportFormat.Srt => "srt",
            _ => "vtt",
        };

        var path = ExportPathResolver.CreateUniqueDownloadPath(suggestedFileName, extension);
        return Task.FromResult<string?>(path);
    }

    public void Reveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true,
                });
                return;
            }

            var folder = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Abrir o Explorer é cortesia; o arquivo já foi gravado.
        }
    }
}
