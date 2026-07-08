using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Verso.Core.Services;

namespace Verso.App.Services;

public sealed class WpfFileOpenService : IFileOpenService
{
    public Task<string?> PickMediaFileAsync()
    {
        var patterns = string.Join(";", UploadMediaFormats.Extensions.Select(ext => "*" + ext));
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar arquivo de áudio ou vídeo",
            Filter = $"Áudio/vídeo ({UploadMediaFormats.DisplayList})|{patterns}|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        var confirmed = dialog.ShowDialog();
        return Task.FromResult(confirmed == true ? dialog.FileName : null);
    }
}
