using Transcriba.App.Services;

namespace Transcriba.Tests.Services;

internal sealed class FakeFileSaveService : IFileSaveService
{
    public string? NextPath { get; set; }
    public ExportFormat? LastFormat { get; private set; }
    public string? LastSuggestedName { get; private set; }

    public Task<string?> PickSavePathAsync(string suggestedFileName, ExportFormat format)
    {
        LastFormat = format;
        LastSuggestedName = suggestedFileName;
        return Task.FromResult(NextPath);
    }
}
