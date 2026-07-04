using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.App.Services;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;
using Transcriba.Core.Services;

namespace Transcriba.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly SidebarViewModel _sidebar;
    private readonly IConfirmationService _confirmation;
    private readonly MediaStorageService _mediaStorage;
    private readonly TranscriptionQueueService? _queueService;

    public ObservableCollection<TranscriptionCardViewModel> Cards { get; } = [];

    [ObservableProperty]
    private LibraryStatusFilter _activeStatusFilter = LibraryStatusFilter.All;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int? _tagFilterId;

    [ObservableProperty]
    private bool _isEmpty;

    public bool IsAllFilterActive => ActiveStatusFilter == LibraryStatusFilter.All;
    public bool IsProgressFilterActive => ActiveStatusFilter == LibraryStatusFilter.Progress;
    public bool IsDoneFilterActive => ActiveStatusFilter == LibraryStatusFilter.Done;

    public DashboardViewModel(
        IServiceScopeFactory scopeFactory,
        IServiceProvider serviceProvider,
        SidebarViewModel sidebar,
        IConfirmationService confirmation,
        MediaStorageService mediaStorage)
    {
        _scopeFactory = scopeFactory;
        _serviceProvider = serviceProvider;
        _sidebar = sidebar;
        _confirmation = confirmation;
        _mediaStorage = mediaStorage;

        if (serviceProvider.GetService<TranscriptionQueueService>() is { } queueService)
        {
            _queueService = queueService;
            _queueService.StatusChanged += OnQueueStatusChanged;
        }
    }

    public void Initialize(NavigationParameter? parameter)
    {
        ApplyNavigationParameter(parameter);
        _ = LoadAsync();
    }

    partial void OnActiveStatusFilterChanged(LibraryStatusFilter value)
    {
        OnPropertyChanged(nameof(IsAllFilterActive));
        OnPropertyChanged(nameof(IsProgressFilterActive));
        OnPropertyChanged(nameof(IsDoneFilterActive));
        _ = LoadAsync();
    }

    partial void OnSearchTextChanged(string value) => _ = LoadAsync();

    [RelayCommand]
    private void SetAllFilter() => ActiveStatusFilter = LibraryStatusFilter.All;

    [RelayCommand]
    private void SetProgressFilter() => ActiveStatusFilter = LibraryStatusFilter.Progress;

    [RelayCommand]
    private void SetDoneFilter() => ActiveStatusFilter = LibraryStatusFilter.Done;

    internal void OpenTranscription(Guid transcriptionId) =>
        _serviceProvider.GetRequiredService<NavigationService>().NavigateTo(
            ScreenKey.Editor,
            new NavigationParameter(TranscriptionId: transcriptionId));

    internal async Task DeleteTranscriptionAsync(Guid transcriptionId)
    {
        var card = FindCard(transcriptionId);
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

        var card = FindCard(transcriptionId);
        if (card is not null)
        {
            card.Status = TranscriptionStatus.InProgress;
            card.ErrorMessage = null;
            card.NotifyRetryAvailability();
        }

        _queueService.Enqueue(new TranscriptionJobRequest(
            transcriptionId,
            transcription.MediaFilePath,
            transcription.Language,
            transcription.Quality,
            settings.Device));
    }

    private void ApplyNavigationParameter(NavigationParameter? parameter)
    {
        if (parameter is null)
        {
            return;
        }

        if (parameter.StatusFilter is LibraryStatusFilter statusFilter)
        {
            ActiveStatusFilter = statusFilter;
        }

        if (parameter.TagId is int tagId)
        {
            TagFilterId = tagId;
            ActiveStatusFilter = LibraryStatusFilter.All;
        }
    }

    private async Task LoadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        var filter = new LibraryFilter(ActiveStatusFilter, TagFilterId);
        var summaries = string.IsNullOrWhiteSpace(SearchText)
            ? await libraryService.GetTranscriptions(filter)
            : await libraryService.SearchText(SearchText, filter);

        Cards.Clear();
        foreach (var summary in summaries)
        {
            Cards.Add(new TranscriptionCardViewModel(
                summary,
                OpenTranscription,
                id => _ = RetryTranscriptionAsync(id),
                id => _ = DeleteTranscriptionAsync(id)));
        }

        IsEmpty = Cards.Count == 0;
    }

    private void OnQueueStatusChanged(object? sender, TranscriptionStatusChangedEventArgs e)
    {
        var card = FindCard(e.TranscriptionId);
        if (card is null)
        {
            if (e.Status is TranscriptionStatusChanged.Queued or TranscriptionStatusChanged.InProgress
                or TranscriptionStatusChanged.Done or TranscriptionStatusChanged.Error)
            {
                _ = LoadAsync();
            }

            return;
        }

        card.Status = MapQueueStatus(e.Status);
        if (e.Status == TranscriptionStatusChanged.Error)
        {
            card.ErrorMessage = e.ErrorMessage;
        }
        else if (e.Status == TranscriptionStatusChanged.Done)
        {
            card.ErrorMessage = null;
            _ = LoadAsync();
        }

        card.NotifyRetryAvailability();
    }

    private TranscriptionCardViewModel? FindCard(Guid transcriptionId)
    {
        foreach (var card in Cards)
        {
            if (card.Id == transcriptionId)
            {
                return card;
            }
        }

        return null;
    }

    private static TranscriptionStatus MapQueueStatus(TranscriptionStatusChanged status) =>
        status switch
        {
            TranscriptionStatusChanged.Done => TranscriptionStatus.Done,
            TranscriptionStatusChanged.Error => TranscriptionStatus.Error,
            _ => TranscriptionStatus.InProgress
        };
}
