using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Photino.NET;
using Verso.Core.Services;

namespace Verso.App.Services;

/// <summary>
/// Seletor de arquivo de mídia cross-platform via diálogo nativo do Photino
/// (<see cref="PhotinoWindow.ShowOpenFileAsync"/>). Substitui o antigo
/// <c>WpfFileOpenService</c> (Microsoft.Win32/OpenFileDialog, Windows-only) para que o
/// app rode em Linux/macOS sem depender do WPF.
///
/// A <see cref="PhotinoWindow"/> é resolvida lazy via <see cref="IServiceProvider"/>: em
/// testes de unidade (sem <c>AddBlazorDesktop</c>, logo sem PhotinoWindow registrada) o
/// método devolve <c>null</c> em vez de quebrar a construção do ViewModel.
/// </summary>
public sealed class PhotinoFileOpenService : IFileOpenService
{
    private readonly IServiceProvider _services;

    public PhotinoFileOpenService(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<string?> PickMediaFileAsync()
    {
        var window = _services.GetService<PhotinoWindow>();
        if (window is null)
            return null;

        var filters = new[]
        {
            ("Áudio/vídeo", UploadMediaFormats.Extensions.Select(e => "*" + e).ToArray()),
            ("Todos os arquivos", new[] { "*.*" }),
        };

        var result = await window.ShowOpenFileAsync(
            title: "Selecionar arquivo de áudio ou vídeo",
            multiSelect: false,
            filters: filters);

        return result is { Length: > 0 } ? result[0] : null;
    }
}