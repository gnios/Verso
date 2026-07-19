using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Verso.App.Services;
using Verso.Core.Services;

namespace Verso.App.ViewModels;

public partial class SidebarViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly FeedbackViewModel _feedback;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ThemeService _themeService;
    private readonly NewPageModalViewModel _newPageModal;
    private readonly IConfirmationService _confirmation;

    public ObservableCollection<SidebarFolderItemViewModel> Folders { get; } = [];
    public ObservableCollection<SidebarTagItemViewModel> Tags { get; } = [];

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _inProgressCount;

    [ObservableProperty]
    private int _doneCount;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _unassignedCount;

    public string ThemeIcon => _themeService.IsDark ? "☀️" : "🌙";

    public SidebarViewModel(
        NavigationService navigation,
        IServiceScopeFactory scopeFactory,
        ThemeService themeService,
        NewPageModalViewModel newPageModal,
        IConfirmationService confirmation,
        FeedbackViewModel feedback)
    {
        _navigation = navigation;
        _scopeFactory = scopeFactory;
        _themeService = themeService;
        _newPageModal = newPageModal;
        _confirmation = confirmation;
        _feedback = feedback;
        _themeService.PropertyChanged += OnThemePropertyChanged;
        // LoadAsync fica a cargo do Sidebar.razor (OnInitializedAsync) para evitar load duplo no startup.
    }

    public async Task LoadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();

        var folders = await folderService.GetAllAsync();
        var tags = await libraryService.GetTagsAsync();

        Folders.Clear();
        foreach (var folder in folders)
        {
            Folders.Add(new SidebarFolderItemViewModel(
                folder,
                _navigation,
                id => _ = DeleteFolderAsync(id)));
        }

        Tags.Clear();
        foreach (var tag in tags)
        {
            Tags.Add(new SidebarTagItemViewModel(tag, _navigation));
        }

        TotalCount = await libraryService.GetCountAsync(new LibraryFilter());
        InProgressCount = await libraryService.GetCountAsync(
            new LibraryFilter(LibraryStatusFilter.Progress));
        DoneCount = await libraryService.GetCountAsync(
            new LibraryFilter(LibraryStatusFilter.Done));
        ErrorCount = await libraryService.GetCountAsync(
            new LibraryFilter(LibraryStatusFilter.Error));
        UnassignedCount = await libraryService.GetCountAsync(
            new LibraryFilter(UnassignedOnly: true));
    }

    [RelayCommand]
    private void NavigateDashboard() =>
        _navigation.NavigateTo(ScreenKey.Dashboard);

    [RelayCommand]
    private void NavigateSettings() =>
        _navigation.NavigateTo(ScreenKey.Settings);


    [RelayCommand]
    private void OpenFeedback() =>
        _feedback.Open();
    [RelayCommand]
    private void NavigateAll() =>
        _navigation.NavigateTo(ScreenKey.Dashboard, new NavigationParameter());

    [RelayCommand]
    private void NavigateInProgress() =>
        _navigation.NavigateTo(
            ScreenKey.Dashboard,
            new NavigationParameter(StatusFilter: LibraryStatusFilter.Progress));

    [RelayCommand]
    private void NavigateDone() =>
        _navigation.NavigateTo(
            ScreenKey.Dashboard,
            new NavigationParameter(StatusFilter: LibraryStatusFilter.Done));

    [RelayCommand]
    private void NavigateError() =>
        _navigation.NavigateTo(
            ScreenKey.Dashboard,
            new NavigationParameter(StatusFilter: LibraryStatusFilter.Error));

    [RelayCommand]
    private void NavigateUnassigned() =>
        _navigation.NavigateTo(
            ScreenKey.Dashboard,
            new NavigationParameter(UnassignedOnly: true));

    [RelayCommand]
    private void NewFolder() => _newPageModal.Open();

    [RelayCommand]
    private void NewTranscription() =>
        _navigation.NavigateTo(ScreenKey.Upload);

    [RelayCommand]
    private async Task ToggleThemeAsync() => await _themeService.ToggleAsync();

    internal async Task DeleteFolderAsync(int folderId)
    {
        using var scope = _scopeFactory.CreateScope();
        var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
        var folder = await folderService.GetByIdAsync(folderId);
        if (folder is null)
        {
            return;
        }

        var count = folder.Transcriptions.Count;
        var countMessage = count switch
        {
            0 => "Nenhuma transcrição está associada.",
            1 => "1 transcrição ficará avulsa na biblioteca.",
            _ => $"{count} transcrições ficarão avulsas na biblioteca.",
        };

        if (!await _confirmation.ConfirmAsync(
                "Excluir pasta",
                $"A pasta \"{folder.Title}\" será excluída. {countMessage} Deseja continuar?"))
        {
            return;
        }

        await folderService.DeleteAsync(folderId);

        if (_navigation.CurrentScreen == ScreenKey.Folder
            && _navigation.NavigationParameter is NavigationParameter parameter
            && parameter.FolderId == folderId)
        {
            _navigation.NavigateTo(ScreenKey.Dashboard);
        }

        await LoadAsync();
    }

    private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeService.IsDark))
        {
            OnPropertyChanged(nameof(ThemeIcon));
        }
    }
}
