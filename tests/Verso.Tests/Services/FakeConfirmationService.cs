using System.Threading.Tasks;
using Verso.App.Services;

namespace Verso.Tests.Services;

internal sealed class FakeConfirmationService : IConfirmationService
{
    public bool NextResult { get; set; } = true;
    public string? LastTitle { get; private set; }
    public string? LastMessage { get; private set; }

    public Task<bool> ConfirmAsync(string title, string message)
    {
        LastTitle = title;
        LastMessage = message;
        return Task.FromResult(NextResult);
    }
}
