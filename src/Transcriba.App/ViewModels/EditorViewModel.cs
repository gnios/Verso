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
using Transcriba.Core.Catalogs;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;
using Transcriba.Core.Export;
using Transcriba.Core.Services;

namespace Transcriba.App.ViewModels;

public partial class EditorViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NavigationService _navigation;
    private readonly SegmentEditingService _segmentEditing;
    private readonly SidebarViewModel _sidebar;
    private Guid _transcriptionId;
    private SegmentItemViewModel? _focusedSegment;
    private int _focusedCaretIndex;
    private TimeSpan _playbackPosition;
    private IReadOnlyList<Segment> _segmentEntities = [];

    public ObservableCollection<SegmentItemViewModel> Segments { get; } = [];
    public ObservableCollection<TranscriptionCardTagViewModel> Tags { get; } = [];
    public IconPickerViewModel IconPicker { get; } = new();

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
    private bool _isSpeakerDropdownOpen;

    public bool HasIcon => !string.IsNullOrWhiteSpace(Icon);
    public bool HasActiveSegment => GetActiveSegmentEntity() is not null;

    public EditorViewModel(
        IServiceScopeFactory scopeFactory,
        NavigationService navigation,
        SegmentEditingService segmentEditing,
        SidebarViewModel sidebar,
        IServiceProvider serviceProvider)
    {
        _scopeFactory = scopeFactory;
        _navigation = navigation;
        _segmentEditing = segmentEditing;
        _sidebar = sidebar;

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
    private void ToggleSpeakerDropdown()
    {
        // stub até T39 — apenas alterna visibilidade
        IsSpeakerDropdownOpen = !IsSpeakerDropdownOpen;
    }

    [RelayCommand]
    private void Export()
    {
        // stub até T48
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
            active.Id);

        await ReloadSegmentsAsync();
        UpdateActiveSegmentHighlight();
    }

    internal void OnSegmentFocused(SegmentItemViewModel segment, int caretIndex)
    {
        _focusedSegment = segment;
        _focusedCaretIndex = caretIndex;
    }

    internal void OnSegmentTextCommitted(SegmentItemViewModel segment, string text)
    {
        _ = PersistSegmentTextAsync(segment.Id, text);
    }

    internal void OnSegmentClicked(SegmentItemViewModel segment)
    {
        // wire seek em T43
        _focusedSegment = segment;
    }

    internal void SetPlaybackPosition(TimeSpan position)
    {
        _playbackPosition = position;
        UpdateActiveSegmentHighlight();
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

        Title = transcription.Title;
        Icon = transcription.Icon;
        IconPicker.SelectedIcon = transcription.Icon ?? IconCatalog.TransIcons[0];

        HasResearchBreadcrumb = transcription.ResearchPage is not null;
        ResearchTitle = transcription.ResearchPage?.Title ?? "";
        ResearchId = transcription.ResearchPageId;

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
            UpdateActiveSegmentHighlight();
        }
        else
        {
            _segmentEntities = [];
            Segments.Clear();
            HasSegments = false;
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
        UpdateActiveSegmentHighlight();
    }

    private void UpdateActiveSegmentHighlight()
    {
        var activeId = GetActiveSegmentId();
        foreach (var segment in Segments)
        {
            segment.IsActive = segment.Id == activeId;
        }
    }

    private void ResetState()
    {
        Segments.Clear();
        Tags.Clear();
        _segmentEntities = [];
        IsInProgress = false;
        IsError = false;
        HasSegments = false;
        StatusMessage = "";
        Title = "";
        Icon = null;
        HasResearchBreadcrumb = false;
        ResearchTitle = "";
        ResearchId = null;
    }

    private void OnQueueStatusChanged(object? sender, TranscriptionStatusChangedEventArgs e)
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
