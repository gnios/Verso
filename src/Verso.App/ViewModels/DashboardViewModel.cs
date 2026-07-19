using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Verso.App.Services;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Services;

namespace Verso.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private const int SearchDebounceMs = 300;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly SidebarViewModel _sidebar;
    private readonly IConfirmationService _confirmation;
    private readonly MediaStorageService _mediaStorage;
    private readonly TranscriptionQueueService? _queueService;
    private CancellationTokenSource? _searchDebounceCts;
    private bool _suppressAutoLoad;
    private bool _disposed;

    public ObservableCollection<TranscriptionCardViewModel> Cards { get; } = [];

    [ObservableProperty]
    private LibraryStatusFilter _activeStatusFilter = LibraryStatusFilter.All;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int? _tagFilterId;

    [ObservableProperty]
    private bool _unassignedOnly;

    [ObservableProperty]
    private bool _isEmpty;
    public bool IsAllFilterActive => ActiveStatusFilter == LibraryStatusFilter.All;
    public bool IsProgressFilterActive => ActiveStatusFilter == LibraryStatusFilter.Progress;
    public bool IsDoneFilterActive => ActiveStatusFilter == LibraryStatusFilter.Done;
    public bool IsErrorFilterActive => ActiveStatusFilter == LibraryStatusFilter.Error;
    public bool IsUnassignedFilterActive => UnassignedOnly;

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
            _queueService.ProgressChanged += OnQueueProgressChanged;
        }
    }

    public void Initialize(NavigationParameter? parameter)
    {
        _suppressAutoLoad = true;
        try
        {
            ApplyNavigationParameter(parameter);
        }
        finally
        {
            _suppressAutoLoad = false;
        }

        _ = LoadAsync();
    }

    partial void OnActiveStatusFilterChanged(LibraryStatusFilter value)
    {
        OnPropertyChanged(nameof(IsAllFilterActive));
        OnPropertyChanged(nameof(IsProgressFilterActive));
        OnPropertyChanged(nameof(IsDoneFilterActive));
        if (!_suppressAutoLoad)
        {
            _ = LoadAsync();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_suppressAutoLoad)
        {
            return;
        }

        _ = DebouncedLoadAsync();
    }

    [RelayCommand]
    private void SetAllFilter() => ActiveStatusFilter = LibraryStatusFilter.All;

    [RelayCommand]
    private void SetProgressFilter() => ActiveStatusFilter = LibraryStatusFilter.Progress;
    [RelayCommand]
    private void SetDoneFilter() => ActiveStatusFilter = LibraryStatusFilter.Done;

    [RelayCommand]
    private void SetErrorFilter() => ActiveStatusFilter = LibraryStatusFilter.Error;

    partial void OnUnassignedOnlyChanged(bool value)
    {
        if (!_suppressAutoLoad)
        {
            _ = LoadAsync();
        }
    }


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
        }

        _queueService.Enqueue(new TranscriptionJobRequest(
            transcriptionId,
            transcription.MediaFilePath,
            transcription.Language,
            transcription.Quality,
            settings.Device,
            settings.MaxTranscriptionThreads));
    }

    internal void CancelTranscription(Guid transcriptionId) =>
        _queueService?.Cancel(transcriptionId);

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

        if (parameter.UnassignedOnly)
        {
            UnassignedOnly = true;
        }

    }

    private async Task DebouncedLoadAsync()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = new CancellationTokenSource();
        var token = _searchDebounceCts.Token;

        try
        {
            await Task.Delay(SearchDebounceMs, token);
            await LoadAsync();
        }
        catch (OperationCanceledException)
        {
            // Digitação contínua — aguarda a próxima pausa.
        }
    }

    private async Task LoadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        var filter = new LibraryFilter(ActiveStatusFilter, TagFilterId, UnassignedOnly);
        var summaries = string.IsNullOrWhiteSpace(SearchText)
            ? await libraryService.GetTranscriptions(filter)
            : await libraryService.SearchText(SearchText, filter);

        if (_disposed)
        {
            return;
        }

        Cards.Clear();
        foreach (var summary in summaries)
        {
            var card = new TranscriptionCardViewModel(
                summary,
                OpenTranscription,
                id => _ = RetryTranscriptionAsync(id),
                id => _ = DeleteTranscriptionAsync(id),
                CancelTranscription);
            RestoreProgress(card);
            Cards.Add(card);
        }

        IsEmpty = Cards.Count == 0;
    }

    private void OnQueueStatusChanged(object? sender, TranscriptionStatusChangedEventArgs e) =>
        UiThread.Invoke(() => ApplyQueueStatusChanged(e));

    private void ApplyQueueStatusChanged(TranscriptionStatusChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

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
            card.ClearProgress();
            _ = _sidebar.LoadAsync();
        }
        else if (e.Status == TranscriptionStatusChanged.Done)
        {
            card.ErrorMessage = null;
            card.ClearProgress();
            _ = LoadAsync();
            _ = _sidebar.LoadAsync();
        }
        else if (e.Status == TranscriptionStatusChanged.Queued)
        {
            card.ClearProgress();
        }
    }

    private void OnQueueProgressChanged(object? sender, TranscriptionProgressEventArgs e) =>
        UiThread.Invoke(() => ApplyQueueProgressChanged(e));

    private void ApplyQueueProgressChanged(TranscriptionProgressEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var card = FindCard(e.TranscriptionId);
        if (card is null || !card.IsInProgress)
        {
            return;
        }

        card.ApplyProgress(e);
    }

    private void RestoreProgress(TranscriptionCardViewModel card)
    {
        if (!card.IsInProgress
            || _queueService is null
            || !_queueService.TryGetLatestProgress(card.Id, out var progress))
        {
            return;
        }

        card.ApplyProgress(progress);
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = null;

        if (_queueService is not null)
        {
            _queueService.StatusChanged -= OnQueueStatusChanged;
            _queueService.ProgressChanged -= OnQueueProgressChanged;
        }
    }
}
