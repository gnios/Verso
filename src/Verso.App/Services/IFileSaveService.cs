using System.Threading.Tasks;

namespace Verso.App.Services;

public interface IFileSaveService
{
    Task<string?> PickSavePathAsync(string suggestedFileName, ExportFormat format);
}
