using Transcriba.App.Services;

namespace Transcriba.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public NavigationService Navigation { get; }
    public SidebarViewModel Sidebar { get; }
    public NewPageModalViewModel NewPageModal { get; }
    public ModelDownloadModalViewModel ModelDownloadModal { get; }

    public MainWindowViewModel(
        NavigationService navigation,
        SidebarViewModel sidebar,
        NewPageModalViewModel newPageModal,
        ModelDownloadModalViewModel modelDownloadModal)
    {
        Navigation = navigation;
        Sidebar = sidebar;
        NewPageModal = newPageModal;
        ModelDownloadModal = modelDownloadModal;

        if (navigation.CurrentViewModel is null)
        {
            navigation.NavigateTo(ScreenKey.Dashboard);
        }
    }
}
