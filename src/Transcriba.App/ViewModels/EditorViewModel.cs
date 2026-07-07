using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.App.Services;
using ExportFormat = Transcriba.App.Services.ExportFormat;
using Transcriba.Core.Catalogs;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;
using Transcriba.Core.Export;
using Transcriba.Core.Media;
using Transcriba.Core.Services;

namespace Transcriba.App.ViewModels;

public partial class EditorViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NavigationService _navigation;
    private readonly SegmentEditingService _segmentEditing;
    private readonly SidebarViewModel _sidebar;
    private readonly IFileSaveService _fileSaveService;
    private Guid _transcriptionId;
    private SegmentItemViewModel? _focusedSegment;
    private int _focusedCaretIndex;
    private TimeSpan _playbackPosition;
    private bool _playbackStarted;
    private IReadOnlyList<Segment> _segmentEntities = [];

    public ObservableCollection<SegmentItemViewModel> Segments { get; } = [];
    public ObservableCollection<TranscriptionCardTagViewModel> Tags { get; } = [];
    public ObservableCollection<ResearchOptionViewModel> ResearchOptions { get; } = [];

    [ObservableProperty]
    private string _newTagInput = "";

    [ObservableProperty]
    private int? _selectedResearchId;
    public IconPickerViewModel IconPicker { get; } = new();
    public SpeakerDropdownViewModel SpeakerDropdown { get; }
    public PlayerBarViewModel PlayerBar { get; }

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
    private bool _hasResearchBreadcrumb;

    [ObservableProperty]
    private string _researchTitle = "";

    [ObservableProperty]
    private int? _researchId;

    [ObservableProperty]
    private string _metaDate = "";

    [ObservableProperty]
    private string _metaDuration = "";

    [ObservableProperty]
    private string _metaSpeakerCount = "";

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
        PlayerBar.PositionChanged += (_, position) => SetPlaybackPosition(position, markStarted: true);

        IconPicker.UseTranscriptionIcons = true;
        IconPicker.AllowNoIcon = true;
        IconPicker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IconPickerViewModel.SelectedIcon) && IsIconPickerOpen)
            {
                _ = ApplyIconAsync(IconPicker.SelectedIcon);
            }
        };

        if (serviceProvider.GetService<TranscriptionQueueService>() is { } queueService)
        {
            queueService.StatusChanged += OnQueueStatusChanged;
        }
    }

    public void Initialize(NavigationParameter? parameter)
    {
        _transcriptionId = parameter?.TranscriptionId ?? Guid.Empty;
        _ = LoadAsync();
    }

    [RelayCommand]
    private void NavigateDashboard() =>
        _navigation.NavigateTo(ScreenKey.Dashboard);

    [RelayCommand]
    private void NavigateResearch()
    {
        if (ResearchId is int researchId)
        {
            _navigation.NavigateTo(
                ScreenKey.Research,
                new NavigationParameter(ResearchId: researchId));
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
        await _sidebar.LoadAsync();
    }

    [RelayCommand]
    private async Task ChangeResearchAsync(int? researchId)
    {
        if (_transcriptionId == Guid.Empty)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var researchService = scope.ServiceProvider.GetRequiredService<ResearchService>();
        await researchService.AssignTranscriptionToResearchAsync(_transcriptionId, researchId);

        SelectedResearchId = researchId;
        if (researchId is null)
        {
            HasResearchBreadcrumb = false;
            ResearchTitle = "";
            ResearchId = null;
        }
        else
        {
            var option = ResearchOptions.FirstOrDefault(o => o.Id == researchId);
            HasResearchBreadcrumb = true;
            ResearchTitle = option?.Name ?? "";
            ResearchId = researchId;
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

        _playbackPosition = position;
        UpdateActiveSegmentHighlight();
        OnPropertyChanged(nameof(HasActiveSegment));
        SpeakerDropdown.NotifyAssignAvailability();
        SpeakerDropdown.RefreshActiveIndicator();
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

    private async Task LoadResearchOptionsAsync(int? selectedId)
    {
        using var scope = _scopeFactory.CreateScope();
        var researchService = scope.ServiceProvider.GetRequiredService<ResearchService>();
        var researches = await researchService.GetAllAsync();

        ResearchOptions.Clear();
        ResearchOptions.Add(new ResearchOptionViewModel { Id = null, Name = "Nenhuma pesquisa", Icon = "" });
        foreach (var r in researches)
        {
            ResearchOptions.Add(new ResearchOptionViewModel { Id = r.Id, Name = r.Title, Icon = r.Icon });
        }

        SelectedResearchId = selectedId;
    }

    private async Task LoadAsync()
    {
        if (_transcriptionId == Guid.Empty)
        {
            ResetState();
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        var transcription = await libraryService.GetTranscriptionDetailAsync(_transcriptionId);

        if (transcription is null)
        {
            return;
        }

        SpeakerDropdown.Initialize(this, _transcriptionId);
        await SpeakerDropdown.LoadSpeakersAsync();
        _playbackStarted = false;

        Title = transcription.Title;
        Icon = transcription.Icon;
        IconPicker.SelectedIcon = transcription.Icon ?? IconCatalog.TransIcons[0];

        HasResearchBreadcrumb = transcription.ResearchPage is not null;
        ResearchTitle = transcription.ResearchPage?.Title ?? "";
        ResearchId = transcription.ResearchPageId;

        await LoadResearchOptionsAsync(transcription.ResearchPageId);

        MetaDate = transcription.CreatedAt.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("pt-BR"));
        MetaDuration = FormatDurationDisplay(transcription.DurationSeconds);
        MetaSpeakerCount = $"{transcription.Speakers.Count} locutor{(transcription.Speakers.Count == 1 ? "" : "es")}";

        Tags.Clear();
        foreach (var tag in transcription.Tags.OrderBy(t => t.Name))
        {
            Tags.Add(new TranscriptionCardTagViewModel(tag.Name, TagColorCatalog.GetColor(tag.Name)));
        }

        IsInProgress = transcription.Status == TranscriptionStatus.InProgress;
        IsError = transcription.Status == TranscriptionStatus.Error;
        StatusMessage = transcription.Status switch
        {
            TranscriptionStatus.InProgress => "Transcrição em andamento…",
            TranscriptionStatus.Error => transcription.ErrorMessage ?? "Erro na transcrição",
            _ => "",
        };

        if (transcription.Status == TranscriptionStatus.Done)
        {
            _segmentEntities = transcription.Segments.OrderBy(s => s.SortOrder).ToList();
            Segments.Clear();
            foreach (var segment in _segmentEntities)
            {
                Segments.Add(new SegmentItemViewModel(this, segment));
            }

            HasSegments = Segments.Count > 0;
            NotifyExportAvailability();
            UpdateActiveSegmentHighlight();
        }
        else
        {
            _segmentEntities = [];
            Segments.Clear();
            HasSegments = false;
            NotifyExportAvailability();
        }

        await PlayerBar.UnloadAsync();
        if (transcription.Status == TranscriptionStatus.Done &&
            !string.IsNullOrWhiteSpace(transcription.MediaFilePath))
        {
            await PlayerBar.LoadAsync(transcription.MediaFilePath);
        }
    }

    private async Task ReloadSegmentsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        var transcription = await libraryService.GetTranscriptionDetailAsync(_transcriptionId);

        if (transcription is null)
        {
            return;
        }

        _segmentEntities = transcription.Segments.OrderBy(s => s.SortOrder).ToList();
        Segments.Clear();
        foreach (var segment in _segmentEntities)
        {
            Segments.Add(new SegmentItemViewModel(this, segment));
        }

        HasSegments = Segments.Count > 0;
        NotifyExportAvailability();
        UpdateActiveSegmentHighlight();
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
        SegmentItemViewModel? activeVm = null;
        foreach (var segment in Segments)
        {
            segment.IsActive = segment.Id == activeId;
            if (segment.IsActive)
            {
                activeVm = segment;
            }
        }

        if (activeVm is not null)
        {
            ScrollToSegmentRequested?.Invoke(this, activeVm);
        }
    }

    private void ResetState()
    {
        Segments.Clear();
        Tags.Clear();
        _segmentEntities = [];
        _playbackStarted = false;
        IsInProgress = false;
        IsError = false;
        HasSegments = false;
        StatusMessage = "";
        Title = "";
        Icon = null;
        HasResearchBreadcrumb = false;
        ResearchTitle = "";
        ResearchId = null;
        ResearchOptions.Clear();
        SelectedResearchId = null;
        NewTagInput = "";
        NotifyExportAvailability();
    }

    private void OnQueueStatusChanged(object? sender, TranscriptionStatusChangedEventArgs e) =>
        UiThread.Invoke(() => ApplyQueueStatusChanged(e));

    private void ApplyQueueStatusChanged(TranscriptionStatusChangedEventArgs e)
    {
        if (e.TranscriptionId != _transcriptionId)
        {
            return;
        }

        if (e.Status is TranscriptionStatusChanged.Done or TranscriptionStatusChanged.Error)
        {
            _ = LoadAsync();
            return;
        }

        IsInProgress = true;
        IsError = false;
        StatusMessage = "Transcrição em andamento…";
        Segments.Clear();
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
}
