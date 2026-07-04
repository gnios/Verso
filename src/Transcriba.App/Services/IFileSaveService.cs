using System.Threading.Tasks;

namespace Transcriba.App.Services;

public interface IFileSaveService
{
    Task<string?> PickSavePathAsync(string suggestedFileName, ExportFormat format);
}
