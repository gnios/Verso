using System.Threading.Tasks;

namespace Verso.App.Services;

public interface IConfirmationService
{
    Task<bool> ConfirmAsync(string title, string message);
}
