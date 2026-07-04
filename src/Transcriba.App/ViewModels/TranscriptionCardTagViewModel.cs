namespace Transcriba.App.ViewModels;

public sealed class TranscriptionCardTagViewModel(string name, string colorName)
{
    public string Name { get; } = name;
    public string ColorName { get; } = colorName;
}
