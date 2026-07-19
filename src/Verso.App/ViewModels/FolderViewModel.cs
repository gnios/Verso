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

public partial class FolderViewModel : ViewModelBase, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NavigationService _navigation;
    private readonly SidebarViewModel _sidebar;
    private readonly IConfirmationService _confirmation;
    private readonly MediaStorageService _mediaStorage;
    private readonly TranscriptionQueueService? _queueService;
    private int _folderId;
    private bool _disposed;

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

    public FolderViewModel(
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
            _queueService.StatusChanged += OnQueueStatusChanged;
            _queueService.ProgressChanged += OnQueueProgressChanged;
        }
    }

    public void Initialize(NavigationParameter? parameter)
    {
        _folderId = parameter?.FolderId ?? 0;
        _ = LoadAsync();
    }

    [RelayCommand]
    private void NavigateDashboard() =>
        _navigation.NavigateTo(ScreenKey.Dashboard);

    [RelayCommand]
    private void AddTranscription() =>
        _navigation.NavigateTo(
            ScreenKey.Upload,
            new NavigationParameter(FolderId: _folderId));

    [RelayCommand]
    private async Task DeleteFolderAsync()
    {
        if (_folderId <= 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
        var folder = await folderService.GetByIdAsync(_folderId);
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

        await folderService.DeleteAsync(_folderId);
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
            settings.Device,
            settings.MaxTranscriptionThreads));
    }

    internal void CancelTranscription(Guid transcriptionId) =>
        _queueService?.Cancel(transcriptionId);

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
            var card = new TranscriptionCardViewModel(
                summary,
                OpenTranscription,
                retryHandler: id => _ = RetryTranscriptionAsync(id),
                deleteHandler: id => _ = DeleteTranscriptionAsync(id),
                cancelHandler: CancelTranscription);
            RestoreProgress(card);
            Transcriptions.Add(card);
        }

        IsEmpty = Transcriptions.Count == 0;
    }

    private async Task LoadAsync()
    {
        if (_folderId <= 0)
        {
            _allSummaries = [];
            Transcriptions.Clear();
            IsEmpty = true;
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        var folder = await folderService.GetByIdAsync(_folderId);
        if (folder is null)
        {
            Title = "";
            Description = "";
            _allSummaries = [];
            Transcriptions.Clear();
            IsEmpty = true;
            return;
        }

        Title = folder.Title;
        Description = folder.Description ?? "";
        Icon = folder.Icon;
        ColorName = folder.ColorName;

        _allSummaries = (await libraryService.GetTranscriptionsForFolderAsync(_folderId)).ToList();
        ApplyFilter();
    }

    private void OnQueueStatusChanged(object? sender, TranscriptionStatusChangedEventArgs e) =>
        UiThread.Invoke(() => ApplyQueueStatusChanged(e));

    private void ApplyQueueStatusChanged(TranscriptionStatusChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var card = Transcriptions.FirstOrDefault(t => t.Id == e.TranscriptionId);
        var summary = _allSummaries.FirstOrDefault(s => s.Id == e.TranscriptionId);

        if (summary is null && card is null)
        {
            if (e.Status is TranscriptionStatusChanged.Queued or TranscriptionStatusChanged.InProgress
                or TranscriptionStatusChanged.Done or TranscriptionStatusChanged.Error)
            {
                _ = LoadAsync();
            }

            return;
        }

        var mapped = MapQueueStatus(e.Status);

        if (summary is not null)
        {
            var index = _allSummaries.FindIndex(s => s.Id == e.TranscriptionId);
            if (index >= 0)
            {
                _allSummaries[index] = summary with
                {
                    Status = mapped,
                    ErrorMessage = e.Status == TranscriptionStatusChanged.Error ? e.ErrorMessage : null,
                };
            }
        }

        if (card is not null)
        {
            card.Status = mapped;
            if (e.Status == TranscriptionStatusChanged.Error)
            {
                card.ErrorMessage = e.ErrorMessage;
                card.ClearProgress();
            }
            else if (e.Status == TranscriptionStatusChanged.Done)
            {
                card.ErrorMessage = null;
                card.ClearProgress();
            }
            else if (e.Status == TranscriptionStatusChanged.Queued)
            {
                card.ClearProgress();
            }
        }

        if (e.Status is TranscriptionStatusChanged.Done or TranscriptionStatusChanged.Error)
        {
            ApplyFilter();
            _ = _sidebar.LoadAsync();
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

        var card = Transcriptions.FirstOrDefault(t => t.Id == e.TranscriptionId);
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

        if (_queueService is not null)
        {
            _queueService.StatusChanged -= OnQueueStatusChanged;
            _queueService.ProgressChanged -= OnQueueProgressChanged;
        }
    }
}
