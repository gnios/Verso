using System.Linq;
using System.Threading.Tasks;
using Verso.Core.Services;

namespace Verso.App.Services;

public sealed class PhotinoFileOpenService : IFileOpenService
{
    private readonly PhotinoWindowAccessor _windowAccessor;

    public PhotinoFileOpenService(PhotinoWindowAccessor windowAccessor)
    {
        _windowAccessor = windowAccessor;
    }

    public async Task<string?> PickMediaFileAsync()
    {
        var filters = new[] { (Name: UploadMediaFormats.DisplayList, Extensions: UploadMediaFormats.Extensions.ToArray()) };
        var result = await _windowAccessor.Window.ShowOpenFileAsync(
            title: "Selecionar arquivo de áudio ou vídeo",
            multiSelect: false,
            filters: filters);

        return result.Length > 0 ? result[0] : null;
    }
}
