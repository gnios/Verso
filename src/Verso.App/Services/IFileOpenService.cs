using System.Threading.Tasks;

namespace Verso.App.Services;

public interface IFileOpenService
{
    Task<string?> PickMediaFileAsync();
}
