using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.App.Services;
using Transcriba.Core.Services;

namespace Transcriba.App.ViewModels;

public partial class ResearchPageViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NavigationService _navigation;
    private int _researchId;

    public ObservableCollection<TranscriptionCardViewModel> Transcriptions { get; } = [];

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _icon = "📚";

    [ObservableProperty]
    private string _colorName = "blue";

    [ObservableProperty]
    private bool _isEmpty;

    public bool IsBlue => ColorName == "blue";
    public bool IsGreen => ColorName == "green";
    public bool IsPurple => ColorName == "purple";
    public bool IsOrange => ColorName == "orange";
    public bool IsPink => ColorName == "pink";
    public bool IsYellow => ColorName == "yellow";
    public bool IsRed => ColorName == "red";
    public bool IsTeal => ColorName == "teal";

    public ResearchPageViewModel(IServiceScopeFactory scopeFactory, NavigationService navigation)
    {
        _scopeFactory = scopeFactory;
        _navigation = navigation;
    }

    public void Initialize(NavigationParameter? parameter)
    {
        _researchId = parameter?.ResearchId ?? 0;
        _ = LoadAsync();
    }

    [RelayCommand]
    private void NavigateDashboard() =>
        _navigation.NavigateTo(ScreenKey.Dashboard);

    [RelayCommand]
    private void AddTranscription() =>
        _navigation.NavigateTo(
            ScreenKey.Upload,
            new NavigationParameter(ResearchId: _researchId));

    internal void OpenTranscription(Guid transcriptionId) =>
        _navigation.NavigateTo(
            ScreenKey.Editor,
            new NavigationParameter(TranscriptionId: transcriptionId));

    partial void OnColorNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsBlue));
        OnPropertyChanged(nameof(IsGreen));
        OnPropertyChanged(nameof(IsPurple));
        OnPropertyChanged(nameof(IsOrange));
        OnPropertyChanged(nameof(IsPink));
        OnPropertyChanged(nameof(IsYellow));
        OnPropertyChanged(nameof(IsRed));
        OnPropertyChanged(nameof(IsTeal));
    }

    private async Task LoadAsync()
    {
        if (_researchId <= 0)
        {
            Transcriptions.Clear();
            IsEmpty = true;
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var researchService = scope.ServiceProvider.GetRequiredService<ResearchService>();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();

        var research = await researchService.GetByIdAsync(_researchId);
        if (research is null)
        {
            Title = "";
            Description = "";
            Transcriptions.Clear();
            IsEmpty = true;
            return;
        }

        Title = research.Title;
        Description = research.Description ?? "";
        Icon = research.Icon;
        ColorName = research.ColorName;

        var summaries = await libraryService.GetTranscriptionsForResearchAsync(_researchId);
        Transcriptions.Clear();
        foreach (var summary in summaries)
        {
            Transcriptions.Add(new TranscriptionCardViewModel(summary, OpenTranscription));
        }

        IsEmpty = Transcriptions.Count == 0;
    }
}
