using System.Collections.Generic;
using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Verso.App.Services;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Services;

namespace Verso.App.ViewModels;

public partial class ResearchPageViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NavigationService _navigation;
    private readonly SidebarViewModel _sidebar;
    private readonly IConfirmationService _confirmation;
    private readonly MediaStorageService _mediaStorage;
    private readonly TranscriptionQueueService? _queueService;
    private int _researchId;

    public ObservableCollection<TranscriptionCardViewModel> Transcriptions { get; } = [];

    private List<TranscriptionSummary> _allSummaries = [];

    [ObservableProperty]
    private LibraryStatusFilter _activeStatusFilter = LibraryStatusFilter.All;

    [ObservableProperty]
    private string _searchText = "";

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

    public bool IsAllFilterActive => ActiveStatusFilter == LibraryStatusFilter.All;
    public bool IsProgressFilterActive => ActiveStatusFilter == LibraryStatusFilter.Progress;
    public bool IsDoneFilterActive => ActiveStatusFilter == LibraryStatusFilter.Done;

    public ResearchPageViewModel(
        IServiceScopeFactory scopeFactory,
        NavigationService navigation,
        SidebarViewModel sidebar,
        IConfirmationService confirmation,
        MediaStorageService mediaStorage,
        IServiceProvider serviceProvider)
    {
        _scopeFactory = scopeFactory;
        _navigation = navigation;
        _sidebar = sidebar;
        _confirmation = confirmation;
        _mediaStorage = mediaStorage;

        if (serviceProvider.GetService<TranscriptionQueueService>() is { } queueService)
        {
            _queueService = queueService;
        }
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

    [RelayCommand]
    private async Task DeleteResearchAsync()
    {
        if (_researchId <= 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var researchService = scope.ServiceProvider.GetRequiredService<ResearchService>();
        var research = await researchService.GetByIdAsync(_researchId);
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

        await researchService.DeleteAsync(_researchId);
        await _sidebar.LoadAsync();
        _navigation.NavigateTo(ScreenKey.Dashboard);
    }

    internal void OpenTranscription(Guid transcriptionId) =>
        _navigation.NavigateTo(
            ScreenKey.Editor,
            new NavigationParameter(TranscriptionId: transcriptionId));

    internal async Task DeleteTranscriptionAsync(Guid transcriptionId)
    {
        var card = Transcriptions.FirstOrDefault(t => t.Id == transcriptionId);
        var title = card?.Title ?? "esta transcrição";

        if (!await _confirmation.ConfirmAsync(
                "Excluir transcrição",
                $"A transcrição \"{title}\" e todos os seus dados serão excluídos permanentemente. Deseja continuar?"))
        {
            return;
        }

        _queueService?.Cancel(transcriptionId);

        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        await libraryService.DeleteTranscriptionAsync(transcriptionId);
        _mediaStorage.DeleteMedia(transcriptionId);
        await _sidebar.LoadAsync();
        await LoadAsync();
    }

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

    partial void OnActiveStatusFilterChanged(LibraryStatusFilter value)
    {
        OnPropertyChanged(nameof(IsAllFilterActive));
        OnPropertyChanged(nameof(IsProgressFilterActive));
        OnPropertyChanged(nameof(IsDoneFilterActive));
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void SetAllFilter() => ActiveStatusFilter = LibraryStatusFilter.All;

    [RelayCommand]
    private void SetProgressFilter() => ActiveStatusFilter = LibraryStatusFilter.Progress;

    [RelayCommand]
    private void SetDoneFilter() => ActiveStatusFilter = LibraryStatusFilter.Done;

    internal async Task RetryTranscriptionAsync(Guid transcriptionId)
    {
        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();

        var transcription = await libraryService.GetTranscriptionAsync(transcriptionId);
        if (transcription is null || string.IsNullOrWhiteSpace(transcription.MediaFilePath) || _queueService is null)
        {
            return;
        }

        var settings = await settingsService.GetAsync();
        await libraryService.ResetToInProgressAsync(transcriptionId);

        var card = Transcriptions.FirstOrDefault(t => t.Id == transcriptionId);
        if (card is not null)
        {
            card.Status = TranscriptionStatus.InProgress;
            card.ErrorMessage = null;
        }

        _queueService.Enqueue(new TranscriptionJobRequest(
            transcriptionId,
            transcription.MediaFilePath,
            transcription.Language,
            transcription.Quality,
            settings.Device));
    }

    /// <summary>Filtra _allSummaries por status + busca e reconstrói a lista de cards exibida.</summary>
    private void ApplyFilter()
    {
        var query = SearchText?.Trim() ?? "";
        var filtered = _allSummaries.AsEnumerable();

        filtered = ActiveStatusFilter switch
        {
            LibraryStatusFilter.Progress => filtered.Where(s => s.Status == TranscriptionStatus.InProgress),
            LibraryStatusFilter.Done => filtered.Where(s => s.Status == TranscriptionStatus.Done),
            _ => filtered,
        };

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(s =>
                s.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (s.Preview?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Transcriptions.Clear();
        foreach (var summary in filtered)
        {
            Transcriptions.Add(new TranscriptionCardViewModel(
                summary,
                OpenTranscription,
                retryHandler: id => _ = RetryTranscriptionAsync(id),
                deleteHandler: id => _ = DeleteTranscriptionAsync(id)));
        }

        IsEmpty = Transcriptions.Count == 0;
    }

    private async Task LoadAsync()
    {
        if (_researchId <= 0)
        {
            _allSummaries = [];
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
            _allSummaries = [];
            Transcriptions.Clear();
            IsEmpty = true;
            return;
        }

        Title = research.Title;
        Description = research.Description ?? "";
        Icon = research.Icon;
        ColorName = research.ColorName;

        _allSummaries = (await libraryService.GetTranscriptionsForResearchAsync(_researchId)).ToList();
        ApplyFilter();
    }
}
