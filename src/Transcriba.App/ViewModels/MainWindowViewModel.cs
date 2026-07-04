using Transcriba.App.Services;

namespace Transcriba.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public NavigationService Navigation { get; }
    public SidebarViewModel Sidebar { get; }
    public NewPageModalViewModel NewPageModal { get; }

    public MainWindowViewModel(
        NavigationService navigation,
        SidebarViewModel sidebar,
        NewPageModalViewModel newPageModal)
    {
        Navigation = navigation;
        Sidebar = sidebar;
        NewPageModal = newPageModal;

        if (navigation.CurrentViewModel is null)
        {
            navigation.NavigateTo(ScreenKey.Dashboard);
        }
    }
}
