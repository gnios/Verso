using System;
using System.IO;
using System.Threading.Tasks;

namespace Verso.App.Services;

public sealed class PhotinoFileSaveService : IFileSaveService
{
    private readonly PhotinoWindowAccessor _windowAccessor;

    public PhotinoFileSaveService(PhotinoWindowAccessor windowAccessor)
    {
        _windowAccessor = windowAccessor;
    }

    public async Task<string?> PickSavePathAsync(string suggestedFileName, ExportFormat format)
    {
        var (extension, description) = format switch
        {
            ExportFormat.Txt => (".txt", "Texto"),
            ExportFormat.Srt => (".srt", "Legendas SRT"),
            _ => (".vtt", "Legendas VTT"),
        };

        var safeName = string.IsNullOrWhiteSpace(suggestedFileName) ? "transcricao" : suggestedFileName.Trim();
        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            safeName + extension);

        var filters = new[] { (Name: description, Extensions: new[] { extension }) };

        return await _windowAccessor.Window.ShowSaveFileAsync(
            title: "Salvar exportação",
            defaultPath: defaultPath,
            filters: filters);
    }
}
