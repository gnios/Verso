using Transcriba.App.Services;

namespace Transcriba.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public NavigationService Navigation { get; }
    public SidebarViewModel Sidebar { get; }

    public MainWindowViewModel(NavigationService navigation, SidebarViewModel sidebar)
    {
        Navigation = navigation;
        Sidebar = sidebar;
    }
}
