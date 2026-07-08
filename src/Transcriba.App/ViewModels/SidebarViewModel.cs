using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.App.Services;
using Transcriba.Core.Services;

namespace Transcriba.App.ViewModels;

public partial class SidebarViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ThemeService _themeService;
    private readonly NewPageModalViewModel _newPageModal;
    private readonly IConfirmationService _confirmation;

    public ObservableCollection<SidebarResearchItemViewModel> Researches { get; } = [];
    public ObservableCollection<SidebarTagItemViewModel> Tags { get; } = [];

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _inProgressCount;

    [ObservableProperty]
    private int _doneCount;

    [ObservableProperty]
    private int _unassignedCount;

    [ObservableProperty]
    private bool _isNewMenuOpen;

    [ObservableProperty]
    private string _searchText = "";

    public string ThemeIcon => _themeService.IsDark ? "☀️" : "🌙";

    public SidebarViewModel(
        NavigationService navigation,
        IServiceScopeFactory scopeFactory,
        ThemeService themeService,
        NewPageModalViewModel newPageModal,
        IConfirmationService confirmation)
    {
        _navigation = navigation;
        _scopeFactory = scopeFactory;
        _themeService = themeService;
        _newPageModal = newPageModal;
        _confirmation = confirmation;
        _themeService.PropertyChanged += OnThemePropertyChanged;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var researchService = scope.ServiceProvider.GetRequiredService<ResearchService>();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();

        var researches = await researchService.GetAllAsync();
        var tags = await libraryService.GetTagsAsync();

        Researches.Clear();
        foreach (var research in researches)
        {
            Researches.Add(new SidebarResearchItemViewModel(
                research,
                _navigation,
                id => _ = DeleteResearchAsync(id)));
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
    private void NavigateUnassigned() =>
        _navigation.NavigateTo(
            ScreenKey.Dashboard,
            new NavigationParameter(UnassignedOnly: true));

    [RelayCommand]
    private void NavigateRecording() =>
        _navigation.NavigateTo(ScreenKey.Recording);

    [RelayCommand]
    private void ToggleNewMenu() =>
        IsNewMenuOpen = !IsNewMenuOpen;

    [RelayCommand]
    private void NewResearch()
    {
        IsNewMenuOpen = false;
        _newPageModal.Open();
    }

    [RelayCommand]
    private void NewTranscription()
    {
        IsNewMenuOpen = false;
        _navigation.NavigateTo(ScreenKey.Upload);
    }

    [RelayCommand]
    private void ImportFile()
    {
        IsNewMenuOpen = false;
        _navigation.NavigateTo(ScreenKey.Upload);
    }

    [RelayCommand]
    private void NewRecording()
    {
        IsNewMenuOpen = false;
        _navigation.NavigateTo(ScreenKey.Recording);
    }

    [RelayCommand]
    private async Task ToggleThemeAsync() => await _themeService.ToggleAsync();

    internal async Task DeleteResearchAsync(int researchId)
    {
        using var scope = _scopeFactory.CreateScope();
        var researchService = scope.ServiceProvider.GetRequiredService<ResearchService>();
        var research = await researchService.GetByIdAsync(researchId);
        if (research is null)
        {
            return;
        }

        var count = research.Transcriptions.Count;
        var countMessage = count switch
        {
            0 => "Nenhuma transcrição está associada.",
            1 => "1 transcrição ficará avulsa na biblioteca.",
            _ => $"{count} transcrições ficarão avulsas na biblioteca.",
        };

        if (!await _confirmation.ConfirmAsync(
                "Excluir pesquisa",
                $"A pesquisa \"{research.Title}\" será excluída. {countMessage} Deseja continuar?"))
        {
            return;
        }

        await researchService.DeleteAsync(researchId);

        if (_navigation.CurrentScreen == ScreenKey.Research
            && _navigation.NavigationParameter is NavigationParameter parameter
            && parameter.ResearchId == researchId)
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
