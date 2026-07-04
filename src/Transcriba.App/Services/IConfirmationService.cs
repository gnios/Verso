using System.Threading.Tasks;

namespace Transcriba.App.Services;

public interface IConfirmationService
{
    Task<bool> ConfirmAsync(string title, string message);
}
