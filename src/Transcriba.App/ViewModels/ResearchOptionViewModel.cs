namespace Transcriba.App.ViewModels;

public sealed class ResearchOptionViewModel
{
    public int? Id { get; init; }

    public string Name { get; init; } = "";

    public override string ToString() => Name;
}
