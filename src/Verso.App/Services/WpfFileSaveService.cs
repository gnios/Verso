using System.Threading.Tasks;
using Microsoft.Win32;

namespace Verso.App.Services;

public sealed class WpfFileSaveService : IFileSaveService
{
    public Task<string?> PickSavePathAsync(string suggestedFileName, ExportFormat format)
    {
        var (extension, description) = format switch
        {
            ExportFormat.Txt => (".txt", "Texto"),
            ExportFormat.Srt => (".srt", "Legendas SRT"),
            _ => (".vtt", "Legendas VTT"),
        };

        var safeName = string.IsNullOrWhiteSpace(suggestedFileName) ? "transcricao" : suggestedFileName.Trim();
        var dialog = new SaveFileDialog
        {
            Title = "Salvar exportação",
            FileName = safeName + extension,
            DefaultExt = extension,
            Filter = $"{description}|*{extension}",
            AddExtension = true,
        };

        var confirmed = dialog.ShowDialog();
        return Task.FromResult(confirmed == true ? dialog.FileName : null);
    }
}
