using Verso.App.Services;

namespace Verso.Tests.Services;

internal sealed class FakeFileSaveService : IFileSaveService
{
    public string? NextPath { get; set; }
    public Exception? ExceptionToThrow { get; set; }
    public ExportFormat? LastFormat { get; private set; }
    public string? LastSuggestedName { get; private set; }

    public Task<string?> PickSavePathAsync(string suggestedFileName, ExportFormat format)
    {
        LastFormat = format;
        LastSuggestedName = suggestedFileName;
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(NextPath);
    }
}
