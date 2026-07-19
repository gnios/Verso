using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Verso.App.Services;
using ExportFormat = Verso.App.Services.ExportFormat;
using Verso.Core.Catalogs;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Export;
using Verso.Core.Media;
using Verso.Core.Services;

namespace Verso.App.ViewModels;

public partial class EditorViewModel : ViewModelBase, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NavigationService _navigation;
    private readonly SegmentEditingService _segmentEditing;
    private readonly SidebarViewModel _sidebar;
    private readonly IFileSaveService _fileSaveService;
    private readonly TranscriptionQueueService? _queueService;
    private Guid _transcriptionId;
    private SegmentItemViewModel? _focusedSegment;
    private int _focusedCaretIndex;
    private TimeSpan _playbackPosition;
    private bool _playbackStarted;
    private Guid? _highlightedSegmentId;
    private bool _disposed;
    private IReadOnlyList<Segment> _segmentEntities = [];

    /// <summary>
    /// Coleção trocada atomicamente via <see cref="ReplaceSegments"/> — evita N× CollectionChanged
    /// (transcrições com 1k+ segmentos congelavam a UI).
    /// </summary>
    public ObservableCollection<SegmentItemViewModel> Segments { get; private set; } = [];
    public ObservableCollection<TranscriptionCardTagViewModel> Tags { get; } = [];
    public ObservableCollection<FolderOptionViewModel> FolderOptions { get; } = [];
    public ObservableCollection<TagOptionViewModel> TagOptions { get; } = [];

    /// <summary>Altura estimada de um item (px) para Virtualize / scroll por índice.</summary>
    public const double SegmentItemHeightEstimate = 110;

    [ObservableProperty]
    private string _newTagInput = "";

    [ObservableProperty]
    private int? _selectedFolderId;
    public IconPickerViewModel IconPicker { get; } = new();
    public SpeakerDropdownViewModel SpeakerDropdown { get; }
    public PlayerBarViewModel PlayerBar { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isInProgress;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _hasSegments;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string? _icon;

    partial void OnIconChanged(string? value) => OnPropertyChanged(nameof(HasIcon));

    [ObservableProperty]
    private bool _hasFolderBreadcrumb;

    [ObservableProperty]
    private string _folderTitle = "";

    [ObservableProperty]
    private int? _folderId;

    [ObservableProperty]
    private string _metaDate = "";

    [ObservableProperty]
    private string _metaDuration = "";

    [ObservableProperty]
    private string _metaProcessingTime = "";

    [ObservableProperty]
    private string _metaSpeakerCount = "";

    [ObservableProperty]
    private string _metaModel = "";

    [ObservableProperty]
    private bool _isIconPickerOpen;

    [ObservableProperty]
    private bool _isExportDialogOpen;

    public bool HasIcon => !string.IsNullOrWhiteSpace(Icon);
    public bool CanExport => HasSegments;
    public bool HasActiveSegment => _playbackStarted && GetActiveSegmentEntity() is not null;

    public EditorViewModel(
        IServiceScopeFactory scopeFactory,
        NavigationService navigation,
        SegmentEditingService segmentEditing,
        SidebarViewModel sidebar,
        IFileSaveService fileSaveService,
        IServiceProvider serviceProvider)
    {
        _scopeFactory = scopeFactory;
        _navigation = navigation;
        _segmentEditing = segmentEditing;
        _sidebar = sidebar;
        _fileSaveService = fileSaveService;
        SpeakerDropdown = new SpeakerDropdownViewModel(scopeFactory);
        PlayerBar = new PlayerBarViewModel(serviceProvider.GetRequiredService<IMediaPlaybackService>());
        PlayerBar.PositionChanged += OnPlayerBarPositionChanged;

        IconPicker.UseTranscriptionIcons = true;
        IconPicker.AllowNoIcon = true;
        IconPicker.PropertyChanged += OnIconPickerPropertyChanged;

        if (serviceProvider.GetService<TranscriptionQueueService>() is { } queueService)
        {
            _queueService = queueService;
            _queueService.StatusChanged += OnQueueStatusChanged;
        }
    }

    private void OnPlayerBarPositionChanged(object? sender, TimeSpan position) =>
        SetPlaybackPosition(position, markStarted: true);

    private void OnIconPickerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IconPickerViewModel.SelectedIcon) && IsIconPickerOpen)
        {
            _ = ApplyIconAsync(IconPicker.SelectedIcon);
        }
    }

    public void Initialize(NavigationParameter? parameter)
    {
        _transcriptionId = parameter?.TranscriptionId ?? Guid.Empty;
        IsLoading = _transcriptionId != Guid.Empty;
        _ = LoadAsync();
    }

    [RelayCommand]
    private void NavigateDashboard() =>
        _navigation.NavigateTo(ScreenKey.Dashboard);

    [RelayCommand]
    private void NavigateFolder()
    {
        if (FolderId is int folderId)
        {
            _navigation.NavigateTo(
                ScreenKey.Folder,
                new NavigationParameter(FolderId: folderId));
        }
    }

    [RelayCommand]
    private void ToggleIconPicker() => IsIconPickerOpen = !IsIconPickerOpen;

    [RelayCommand]
    private void CloseIconPicker() => IsIconPickerOpen = false;

    [RelayCommand]
    private void ToggleSpeakerDropdown() => SpeakerDropdown.ToggleCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export() => IsExportDialogOpen = true;

    [RelayCommand]
    private void CloseExportDialog() => IsExportDialogOpen = false;

    [RelayCommand]
    private Task ExportAsTxtAsync() => ExportWithFormatAsync(ExportFormat.Txt);

    [RelayCommand]
    private Task ExportAsSrtAsync() => ExportWithFormatAsync(ExportFormat.Srt);

    [RelayCommand]
    private Task ExportAsVttAsync() => ExportWithFormatAsync(ExportFormat.Vtt);

    internal async Task ExportWithFormatAsync(ExportFormat format)
    {
        if (!HasSegments || _transcriptionId == Guid.Empty)
        {
            return;
        }

        IsExportDialogOpen = false;

        var destPath = await _fileSaveService.PickSavePathAsync(Title, format);
        if (destPath is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var exportService = scope.ServiceProvider.GetRequiredService<ExportService>();

        switch (format)
        {
            case ExportFormat.Txt:
                await exportService.ExportTxtAsync(_transcriptionId, destPath);
                break;
            case ExportFormat.Srt:
                await exportService.ExportSrtAsync(_transcriptionId, destPath);
                break;
            case ExportFormat.Vtt:
                await exportService.ExportVttAsync(_transcriptionId, destPath);
                break;
        }
    }

    [RelayCommand]
    private async Task CommitTitleAsync()
    {
        if (_transcriptionId == Guid.Empty || string.IsNullOrWhiteSpace(Title))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        await libraryService.UpdateTranscriptionTitleAsync(_transcriptionId, Title.Trim());
        await _sidebar.LoadAsync();
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        var name = NewTagInput?.Trim();
        if (string.IsNullOrWhiteSpace(name) || _transcriptionId == Guid.Empty)
        {
            NewTagInput = "";
            return;
        }

        var current = Tags.Select(t => t.Name).ToList();
        if (current.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            NewTagInput = "";
            return;
        }

        var newSet = current.Append(name).ToList();
        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        await libraryService.UpdateTranscriptionTagsAsync(_transcriptionId, newSet);

        Tags.Add(new TranscriptionCardTagViewModel(name, TagColorCatalog.GetColor(name)));
        NewTagInput = "";
        await LoadTagOptionsAsync();
        await _sidebar.LoadAsync();
    }

    [RelayCommand]
    private async Task RemoveTagAsync(TranscriptionCardTagViewModel tag)
    {
        if (_transcriptionId == Guid.Empty || tag is null)
        {
            return;
        }

        var newSet = Tags
            .Where(t => !string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .ToList();

        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        await libraryService.UpdateTranscriptionTagsAsync(_transcriptionId, newSet);
        Tags.Remove(tag);
        await LoadTagOptionsAsync();
        await _sidebar.LoadAsync();
    }

    [RelayCommand]
    private async Task ChangeFolderAsync(int? folderId)
    {
        if (_transcriptionId == Guid.Empty)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
        await folderService.AssignTranscriptionToFolderAsync(_transcriptionId, folderId);

        SelectedFolderId = folderId;
        if (folderId is null)
        {
            HasFolderBreadcrumb = false;
            FolderTitle = "";
            FolderId = null;
        }
        else
        {
            var option = FolderOptions.FirstOrDefault(o => o.Id == folderId);
            HasFolderBreadcrumb = true;
            FolderTitle = option?.Name ?? "";
            FolderId = folderId;
        }

        await _sidebar.LoadAsync();
    }

    [RelayCommand]
    private async Task SplitSegmentAsync()
    {
        if (_focusedSegment is null)
        {
            return;
        }

        var entity = _segmentEntities.FirstOrDefault(s => s.Id == _focusedSegment.Id);
        if (entity is null)
        {
            return;
        }

        entity.Text = _focusedSegment.Text;
        var split = _segmentEditing.SplitAtCaret(entity, _focusedCaretIndex);
        if (split is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        await libraryService.ApplySegmentSplitAsync(
            _transcriptionId,
            entity.Id,
            split.Value.Before.Text,
            split.Value.Before.EndSeconds,
            split.Value.After);

        await ReloadSegmentsAsync();
    }

    [RelayCommand]
    private async Task MergeSegmentAsync()
    {
        var active = GetActiveSegmentEntity();
        if (active is null)
        {
            return;
        }

        var activeVm = Segments.FirstOrDefault(x => x.Id == active.Id);
        if (activeVm is not null)
        {
            active.Text = activeVm.Text;
        }
        var merged = _segmentEditing.MergeWithPrevious(_segmentEntities, active);
        if (merged is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        await libraryService.ApplySegmentMergeAsync(
            _transcriptionId,
            merged.Id,
            merged.Text,
            active.EndSeconds,
            active.Id);

        await ReloadSegmentsAsync();
        UpdateActiveSegmentHighlight();
    }

    internal void OnSegmentFocused(SegmentItemViewModel segment, int caretIndex)
    {
        _focusedSegment = segment;
        _focusedCaretIndex = caretIndex;
    }

    // Ações por segmento (hover toolbar no SegmentItem). Diferem dos comandos
    // parameterless acima (que operam sobre o segmento focado/ativo de playback):
    // estas recebem o segmento explícito clicado no hover.
    internal async Task SplitSegmentForAsync(SegmentItemViewModel segment)
    {
        var entity = _segmentEntities.FirstOrDefault(s => s.Id == segment.Id);
        if (entity is null)
        {
            return;
        }

        // Sincroniza o texto do segmento (que pode ter edição não commitada, já que o
        // clique no Dividir não dispara o blur do textarea) antes de dividir.
        entity.Text = segment.Text;
        var split = _segmentEditing.SplitAtCaret(entity, segment.CaretIndex);
        if (split is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        await libraryService.ApplySegmentSplitAsync(
            _transcriptionId,
            entity.Id,
            split.Value.Before.Text,
            split.Value.Before.EndSeconds,
            split.Value.After);

        await ReloadSegmentsAsync();
    }

    internal async Task MergeSegmentForAsync(SegmentItemViewModel segment)
    {
        var entity = _segmentEntities.FirstOrDefault(s => s.Id == segment.Id);
        if (entity is null)
        {
            return;
        }

        // Sincroniza o texto (edição não commitada, já que Mesclar não dispara blur).
        entity.Text = segment.Text;
        var merged = _segmentEditing.MergeWithPrevious(_segmentEntities, entity);
        if (merged is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        await libraryService.ApplySegmentMergeAsync(
            _transcriptionId,
            merged.Id,
            merged.Text,
            entity.EndSeconds,
            entity.Id);

        await ReloadSegmentsAsync();
        UpdateActiveSegmentHighlight();
    }

    internal void OpenLocutorFor(SegmentItemViewModel segment)
    {
        // Posiciona o playback no início do segmento — ele vira o segmento "ativo" de
        // playback, que é o alvo do SpeakerDropdown existente (CanAssign/assign usam o
        // segmento ativo). Assim o dropdown de locutor reaproveitado atribui a este
        // segmento sem refatorar o SpeakerDropdownViewModel.
        SetPlaybackPosition(TimeSpan.FromSeconds(segment.StartSeconds), markStarted: true);
        SpeakerDropdown.IsOpen = true;
    }

    internal void OnSegmentTextCommitted(SegmentItemViewModel segment, string text)
    {
        _ = PersistSegmentTextAsync(segment.Id, text);
    }

    internal event EventHandler<double>? SegmentSeekRequested;

    internal event EventHandler<SegmentItemViewModel>? ScrollToSegmentRequested;

    internal void OnSegmentClicked(SegmentItemViewModel segment)
    {
        _focusedSegment = segment;
        SpeakerDropdown.IsOpen = false;
        SegmentSeekRequested?.Invoke(this, segment.StartSeconds);
        PlayerBar.SeekToTime(TimeSpan.FromSeconds(segment.StartSeconds));
        SetPlaybackPosition(TimeSpan.FromSeconds(segment.StartSeconds), markStarted: true);
    }

    internal void OnSpeakerRenamed(Guid speakerId, string newName)
    {
        foreach (var segment in Segments)
        {
            if (segment.SpeakerId == speakerId)
            {
                segment.RenameSpeaker(newName);
            }
        }
    }

    internal async Task AssignSpeakerToActiveSegmentAsync(Guid speakerId)
    {
        var active = GetActiveSegmentEntity();
        if (active is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        var speakerService = scope.ServiceProvider.GetRequiredService<SpeakerService>();
        await libraryService.AssignSpeakerToSegmentAsync(active.Id, speakerId);

        var speaker = (await speakerService.GetSpeakersAsync(_transcriptionId))
            .First(s => s.Id == speakerId);
        active.SpeakerId = speakerId;
        active.Speaker = speaker;

        var segmentVm = Segments.FirstOrDefault(s => s.Id == active.Id);
        segmentVm?.UpdateSpeaker(speaker);
        OnPropertyChanged(nameof(HasActiveSegment));
        SpeakerDropdown.NotifyAssignAvailability();
        SpeakerDropdown.RefreshActiveIndicator();
    }

    internal void SetPlaybackPosition(TimeSpan position, bool markStarted = false)
    {
        if (markStarted)
        {
            _playbackStarted = true;
        }

        var hadActive = HasActiveSegment;
        var previousHighlighted = _highlightedSegmentId;
        _playbackPosition = position;
        UpdateActiveSegmentHighlight();

        // Evita cascata de PropertyChanged/re-render a cada tick do áudio.
        if (previousHighlighted != _highlightedSegmentId || hadActive != HasActiveSegment)
        {
            OnPropertyChanged(nameof(HasActiveSegment));
            SpeakerDropdown.NotifyAssignAvailability();
            SpeakerDropdown.RefreshActiveIndicator();
        }
    }

    internal Segment? GetActiveSegmentEntity() =>
        _segmentEditing.GetActiveSegment(_segmentEntities, _playbackPosition);

    internal Guid? GetActiveSegmentId() => GetActiveSegmentEntity()?.Id;

    internal static string FormatSegmentTime(double seconds)
    {
        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
    }

    private async Task PersistSegmentTextAsync(Guid segmentId, string text)
    {
        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        await libraryService.UpdateSegmentTextAsync(segmentId, text);

        var entity = _segmentEntities.FirstOrDefault(s => s.Id == segmentId);
        if (entity is not null)
        {
            entity.Text = text;
        }
    }

    private async Task ApplyIconAsync(string? icon)
    {
        if (_transcriptionId == Guid.Empty)
        {
            return;
        }

        Icon = icon;
        IsIconPickerOpen = false;

        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        await libraryService.UpdateTranscriptionIconAsync(_transcriptionId, icon);
        await _sidebar.LoadAsync();
    }

    private async Task LoadFolderOptionsAsync(int? selectedId)
    {
        using var scope = _scopeFactory.CreateScope();
        var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
        var folders = await folderService.GetAllAsync().ConfigureAwait(false);
        if (_disposed)
        {
            return;
        }

        await UiThread.InvokeAsync(() =>
        {
            FolderOptions.Clear();
            FolderOptions.Add(new FolderOptionViewModel { Id = null, Name = "Nenhuma pasta", Icon = "" });
            foreach (var f in folders)
            {
                FolderOptions.Add(new FolderOptionViewModel { Id = f.Id, Name = f.Title, Icon = f.Icon });
            }

            SelectedFolderId = selectedId;
        });
    }

    private async Task LoadTagOptionsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        var tags = await libraryService.GetTagsAsync().ConfigureAwait(false);
        if (_disposed)
        {
            return;
        }

        await UiThread.InvokeAsync(() =>
        {
            TagOptions.Clear();
            foreach (var t in tags)
            {
                TagOptions.Add(new TagOptionViewModel
                {
                    Id = t.Id,
                    Name = t.Name,
                    ColorName = TagColorCatalog.GetColor(t.Name),
                });
            }
        });
    }

    private async Task LoadAsync()
    {
        if (_transcriptionId == Guid.Empty)
        {
            ResetState();
            IsLoading = false;
            return;
        }

        IsLoading = true;
        try
        {
            // ConfigureAwait(false): montar milhares de VMs fora da UI thread.
            using var scope = _scopeFactory.CreateScope();
            var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
            var transcription = await libraryService.GetTranscriptionDetailAsync(_transcriptionId)
                .ConfigureAwait(false);

            if (_disposed || transcription is null)
            {
                return;
            }

            IReadOnlyList<Segment> entities = [];
            List<SegmentItemViewModel> segmentVms = [];
            if (transcription.Status == TranscriptionStatus.Done)
            {
                entities = transcription.Segments.OrderBy(s => s.SortOrder).ToList();
                segmentVms = new List<SegmentItemViewModel>(entities.Count);
                foreach (var segment in entities)
                {
                    segmentVms.Add(new SegmentItemViewModel(this, segment));
                }
            }

            var tagVms = transcription.Tags
                .OrderBy(t => t.Name)
                .Select(t => new TranscriptionCardTagViewModel(t.Name, TagColorCatalog.GetColor(t.Name)))
                .ToList();

            await UiThread.InvokeAsync(() =>
                ApplyLoadedTranscription(transcription, entities, segmentVms, tagVms));

            if (_disposed)
            {
                return;
            }

            await Task.WhenAll(
                LoadFolderOptionsAsync(transcription.FolderId),
                LoadTagOptionsAsync());

            var mediaPath = transcription.Status == TranscriptionStatus.Done
                ? transcription.MediaFilePath
                : null;
            var knownDuration = transcription.DurationSeconds > 0
                ? TimeSpan.FromSeconds(transcription.DurationSeconds)
                : (TimeSpan?)null;
            _ = LoadPlaybackAsync(mediaPath, knownDuration);
        }
        finally
        {
            if (!_disposed)
            {
                IsLoading = false;
            }
        }
    }

    private void ApplyLoadedTranscription(
        Transcription transcription,
        IReadOnlyList<Segment> entities,
        List<SegmentItemViewModel> segmentVms,
        List<TranscriptionCardTagViewModel> tagVms)
    {
        SpeakerDropdown.Initialize(this, _transcriptionId);
        SpeakerDropdown.SetSpeakers(transcription.Speakers);
        _playbackStarted = false;
        _highlightedSegmentId = null;

        Title = transcription.Title;
        Icon = transcription.Icon;
        IconPicker.SelectedIcon = transcription.Icon ?? IconCatalog.TransIcons[0];

        HasFolderBreadcrumb = transcription.Folder is not null;
        FolderTitle = transcription.Folder?.Title ?? "";
        FolderId = transcription.FolderId;

        MetaDate = transcription.CreatedAt.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("pt-BR"));
        MetaDuration = FormatDurationDisplay(transcription.DurationSeconds);
        MetaProcessingTime = FormatProcessingTime(transcription.ProcessingDurationSeconds);
        MetaSpeakerCount = $"{transcription.Speakers.Count} locutor{(transcription.Speakers.Count == 1 ? "" : "es")}";
        MetaModel = $"{ModelCatalog.Find(transcription.Quality).Label} · {DeviceDisplayName(transcription.Device)}";

        Tags.Clear();
        foreach (var tag in tagVms)
        {
            Tags.Add(tag);
        }

        IsInProgress = transcription.Status == TranscriptionStatus.InProgress;
        IsError = transcription.Status == TranscriptionStatus.Error;
        StatusMessage = transcription.Status switch
        {
            TranscriptionStatus.InProgress => "Transcrição em andamento…",
            TranscriptionStatus.Error => transcription.ErrorMessage ?? "Erro na transcrição",
            _ => "",
        };

        _segmentEntities = entities;
        ReplaceSegments(segmentVms);
        HasSegments = Segments.Count > 0;
        NotifyExportAvailability();
        IsLoading = false;
    }

    private void ReplaceSegments(IEnumerable<SegmentItemViewModel> items)
    {
        Segments = new ObservableCollection<SegmentItemViewModel>(items);
        OnPropertyChanged(nameof(Segments));
    }

    private async Task LoadPlaybackAsync(string? mediaPath, TimeSpan? knownDuration)
    {
        try
        {
            await PlayerBar.UnloadAsync();
            if (!string.IsNullOrWhiteSpace(mediaPath) && !_disposed)
            {
                await PlayerBar.LoadAsync(mediaPath, knownDuration);
            }
        }
        catch (Exception)
        {
            // Falha de load do áudio não deve derrubar a tela já aberta.
        }
    }

    private async Task ReloadSegmentsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        var transcription = await libraryService.GetTranscriptionDetailAsync(_transcriptionId)
            .ConfigureAwait(false);

        if (transcription is null || _disposed)
        {
            return;
        }

        var entities = transcription.Segments.OrderBy(s => s.SortOrder).ToList();
        var vms = new List<SegmentItemViewModel>(entities.Count);
        foreach (var segment in entities)
        {
            vms.Add(new SegmentItemViewModel(this, segment));
        }

        await UiThread.InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            _segmentEntities = entities;
            ReplaceSegments(vms);
            HasSegments = Segments.Count > 0;
            NotifyExportAvailability();
            UpdateActiveSegmentHighlight();
        });
    }

    partial void OnHasSegmentsChanged(bool value) => NotifyExportAvailability();

    private void NotifyExportAvailability()
    {
        OnPropertyChanged(nameof(CanExport));
        ExportCommand.NotifyCanExecuteChanged();
    }

    private void UpdateActiveSegmentHighlight()
    {
        var activeId = GetActiveSegmentId();
        if (activeId == _highlightedSegmentId)
        {
            return;
        }

        if (_highlightedSegmentId is Guid previousId)
        {
            var previous = FindSegmentVm(previousId);
            if (previous is not null)
            {
                previous.IsActive = false;
            }
        }

        _highlightedSegmentId = activeId;

        SegmentItemViewModel? activeVm = null;
        if (activeId is Guid id)
        {
            activeVm = FindSegmentVm(id);
            if (activeVm is not null)
            {
                activeVm.IsActive = true;
            }
        }

        if (activeVm is not null)
        {
            ScrollToSegmentRequested?.Invoke(this, activeVm);
        }
    }

    private SegmentItemViewModel? FindSegmentVm(Guid id)
    {
        foreach (var segment in Segments)
        {
            if (segment.Id == id)
            {
                return segment;
            }
        }

        return null;
    }

    internal int IndexOfSegment(SegmentItemViewModel segment)
    {
        for (var i = 0; i < Segments.Count; i++)
        {
            if (ReferenceEquals(Segments[i], segment) || Segments[i].Id == segment.Id)
            {
                return i;
            }
        }

        return -1;
    }

    private void ResetState()
    {
        ReplaceSegments([]);
        Tags.Clear();
        _segmentEntities = [];
        _playbackStarted = false;
        _highlightedSegmentId = null;
        IsInProgress = false;
        IsError = false;
        HasSegments = false;
        StatusMessage = "";
        Title = "";
        Icon = null;
        HasFolderBreadcrumb = false;
        FolderTitle = "";
        FolderId = null;
        FolderOptions.Clear();
        SelectedFolderId = null;
        TagOptions.Clear();
        NewTagInput = "";
        NotifyExportAvailability();
    }

    private void OnQueueStatusChanged(object? sender, TranscriptionStatusChangedEventArgs e) =>
        UiThread.Invoke(() => ApplyQueueStatusChanged(e));

    private void ApplyQueueStatusChanged(TranscriptionStatusChangedEventArgs e)
    {
        if (_disposed || e.TranscriptionId != _transcriptionId)
        {
            return;
        }

        if (e.Status is TranscriptionStatusChanged.Done or TranscriptionStatusChanged.Error)
        {
            _ = LoadAsync();
            _ = _sidebar.LoadAsync();
            return;
        }

        IsInProgress = true;
        IsError = false;
        StatusMessage = "Transcrição em andamento…";
        ReplaceSegments([]);
        HasSegments = false;
        NotifyExportAvailability();
        _ = PlayerBar.UnloadAsync();
    }

    private static string FormatDurationDisplay(double seconds)
    {
        if (seconds <= 0)
        {
            return "—";
        }

        var duration = TimeSpan.FromSeconds(seconds);
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}min";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes} min";
        }

        return TranscriptionTextFormatter.FormatDuration(duration);
    }

    /// <summary>
    /// Formata o tempo de processamento da transcrição para exibição elegante no meta.
    /// null/vazio → ""; senão "Xh Ymin", "Ymin Zs" ou "Zs" (arredondado).
    /// </summary>
    private static string FormatProcessingTime(double? seconds)
    {
        if (!seconds.HasValue || seconds.Value <= 0)
        {
            return "";
        }

        var span = TimeSpan.FromSeconds(seconds.Value);
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}min";
        }

        if (span.TotalMinutes >= 1)
        {
            return $"{(int)span.TotalMinutes}min {span.Seconds}s";
        }

        return $"{Math.Round(span.TotalSeconds)}s";
    }

    private static string DeviceDisplayName(ExecutionDevice device) => device switch
    {
        ExecutionDevice.Cpu => "CPU",
        ExecutionDevice.Cuda => "CUDA",
        ExecutionDevice.Vulkan => "Vulkan",
        ExecutionDevice.CoreMl => "Core ML",
        ExecutionDevice.Auto => "Auto",
        _ => device.ToString(),
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
        }

        PlayerBar.PositionChanged -= OnPlayerBarPositionChanged;
        IconPicker.PropertyChanged -= OnIconPickerPropertyChanged;
        PlayerBar.Dispose();
    }
}
