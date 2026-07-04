using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Transcriba.App.Services;

public sealed class AvaloniaFileSaveService : IFileSaveService
{
    private Func<TopLevel?> _getTopLevel = () =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public void SetTopLevelProvider(Func<TopLevel?> provider) => _getTopLevel = provider;

    public async Task<string?> PickSavePathAsync(string suggestedFileName, ExportFormat format)
    {
        var topLevel = _getTopLevel();
        if (topLevel?.StorageProvider is null)
        {
            return null;
        }

        var (extension, description) = format switch
        {
            ExportFormat.Txt => (".txt", "Texto"),
            ExportFormat.Srt => (".srt", "Legendas SRT"),
            _ => (".vtt", "Legendas VTT"),
        };

        var safeName = string.IsNullOrWhiteSpace(suggestedFileName) ? "transcricao" : suggestedFileName.Trim();
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar exportação",
            SuggestedFileName = safeName + extension,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices =
            [
                new FilePickerFileType(description)
                {
                    Patterns = [$"*{extension}"],
                }
            ],
        });

        return file?.TryGetLocalPath();
    }
}
