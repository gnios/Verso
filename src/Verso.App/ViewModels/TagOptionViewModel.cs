namespace Verso.App.ViewModels;

/// <summary>
/// Tag global disponível para atribuição no Editor (vinda de LibraryService.GetTagsAsync).
/// Carrega apenas o necessário para o TagCombobox: id, nome e cor do catálogo.
/// </summary>
public sealed class TagOptionViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string ColorName { get; init; } = "blue";
    public override string ToString() => Name;
}