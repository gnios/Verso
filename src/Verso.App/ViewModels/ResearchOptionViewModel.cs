namespace Verso.App.ViewModels;

public sealed class ResearchOptionViewModel
{
    public int? Id { get; init; }

    public string Name { get; init; } = "";
    public string Icon { get; init; } = "";
    public override string ToString() => Name;
}
