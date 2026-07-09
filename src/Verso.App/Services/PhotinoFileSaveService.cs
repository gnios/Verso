using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Photino.NET;

namespace Verso.App.Services;

/// <summary>
/// Seletor de caminho de salvamento cross-platform via diálogo nativo do Photino
/// (<see cref="PhotinoWindow.ShowSaveFileAsync"/>). Substitui o antigo
/// <c>WpfFileSaveService</c> (Microsoft.Win32/SaveFileDialog, Windows-only) para que o
/// app rode em Linux/macOS sem depender do WPF.
///
/// A <see cref="PhotinoWindow"/> é resolvida lazy (ver <see cref="PhotinoFileOpenService"/>).
/// </summary>
public sealed class PhotinoFileSaveService : IFileSaveService
{
    private readonly IServiceProvider _services;

    public PhotinoFileSaveService(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<string?> PickSavePathAsync(string suggestedFileName, ExportFormat format)
    {
        var window = _services.GetService<PhotinoWindow>();
        if (window is null)
            return null;

        var (extension, description) = format switch
        {
            ExportFormat.Txt => (".txt", "Texto"),
            ExportFormat.Srt => (".srt", "Legendas SRT"),
            _ => (".vtt", "Legendas VTT"),
        };

        var safeName = string.IsNullOrWhiteSpace(suggestedFileName) ? "transcricao" : suggestedFileName.Trim();

        var filters = new[]
        {
            (description, new[] { "*" + extension }),
        };

        return await window.ShowSaveFileAsync(
            title: "Salvar exportação",
            defaultPath: safeName + extension,
            filters: filters);
    }
}