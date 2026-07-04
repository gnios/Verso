using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Transcriba.App.Services;
using Transcriba.Core.Catalogs;
using Transcriba.Core.Services;

namespace Transcriba.App.ViewModels;

public partial class SidebarTagItemViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;

    public int Id { get; }
    public string Name { get; }
    public int Count { get; }
    public string ColorName { get; }

    public SidebarTagItemViewModel(TagSummary tag, NavigationService navigation)
    {
        _navigation = navigation;
        Id = tag.Id;
        Name = tag.Name;
        Count = tag.Count;
        ColorName = TagColorCatalog.GetColor(tag.Name);
    }

    [RelayCommand]
    private void Navigate() =>
        _navigation.NavigateTo(
            ScreenKey.Dashboard,
            new NavigationParameter(TagId: Id));
}
