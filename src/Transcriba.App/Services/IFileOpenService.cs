using System.Threading.Tasks;

namespace Transcriba.App.Services;

public interface IFileOpenService
{
    Task<string?> PickMediaFileAsync();
}
