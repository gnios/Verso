using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.App.Services;
using Transcriba.Core.Catalogs;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;
using Transcriba.Core.Services;

namespace Transcriba.App.ViewModels;

public partial class UploadViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IServiceProvider _services;
    private readonly NavigationService _navigation;
    private readonly MediaStorageService _mediaStorage;
    private readonly TranscriptionQueueService _queueService;

    public ObservableCollection<ResearchOptionViewModel> ResearchOptions { get; } = [];

    public IconPickerViewModel IconPicker { get; } = new();


    public IReadOnlyList<LanguageOptionViewModel> LanguageOptions { get; } =
    [
        new("pt", "Português (Brasil)"),
        new("es", "Español"),
        new("en", "English"),
    ];

    public IReadOnlyList<QualityOptionViewModel> QualityOptions { get; } =
    [
        new(ModelQuality.Standard, "Padrão"),
        new(ModelQuality.High, "Alta"),
    ];

    public IReadOnlyList<SpeakerModeOptionViewModel> SpeakerModeOptions { get; } =
    [
        new(SpeakerMode.Automatic, "Automático"),
        new(SpeakerMode.Off, "Desativado"),
    ];

    [ObservableProperty]
    private string? _selectedFilePath;

    [ObservableProperty]
    private string _selectedFileName = "";

    [ObservableProperty]
    private string _selectedFileSize = "";

    [ObservableProperty]
    private bool _isDragOver;

    [ObservableProperty]
    private string? _validationError;

    [ObservableProperty]
    private string _language = "pt";

    [ObservableProperty]
    private LanguageOptionViewModel? _selectedLanguageOption;

    [ObservableProperty]
    private ModelQuality _quality = ModelQuality.Standard;

    [ObservableProperty]
    private QualityOptionViewModel? _selectedQualityOption;

    [ObservableProperty]
    private SpeakerMode _speakerMode = SpeakerMode.Automatic;

    [ObservableProperty]
    private SpeakerModeOptionViewModel? _selectedSpeakerModeOption;

    [ObservableProperty]
    private ResearchOptionViewModel? _selectedResearch;

    [ObservableProperty]
    private bool _isStarting;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _tagsText = "";


    public bool HasSelectedFile => !string.IsNullOrWhiteSpace(SelectedFilePath);
    public bool CanStart => HasSelectedFile && !IsStarting && !string.IsNullOrWhiteSpace(Title);

    public string SupportedFormatsLabel => UploadMediaFormats.DisplayList;

    public UploadViewModel(
        IServiceScopeFactory scopeFactory,
        IServiceProvider services,
        NavigationService navigation,
        MediaStorageService mediaStorage,
        TranscriptionQueueService queueService)
    {
        _scopeFactory = scopeFactory;
        _services = services;
        _navigation = navigation;
        _mediaStorage = mediaStorage;
        _queueService = queueService;
    }

    public void Initialize(NavigationParameter? parameter)
    {
        ClearSelectedFile();
        ValidationError = null;
        IsDragOver = false;
        IsStarting = false;
        Title = "";
        TagsText = "";
        IconPicker.UseTranscriptionIcons = true;
        IconPicker.AllowNoIcon = false;
        IconPicker.SelectedIcon = IconCatalog.TransIcons[0];
        _ = LoadFormAsync(parameter?.ResearchId);
    }

    public bool TrySelectFile(string path)
    {
        ValidationError = null;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ClearSelectedFile();
            ValidationError = "Arquivo não encontrado.";
            return false;
        }

        if (!UploadMediaFormats.IsSupported(path))
        {
            ClearSelectedFile();
            ValidationError = $"Formato não suportado. Use: {UploadMediaFormats.DisplayList}.";
            return false;
        }

        SelectedFilePath = path;
        SelectedFileName = Path.GetFileName(path);
        SelectedFileSize = FormatFileSize(new FileInfo(path).Length);
        if (string.IsNullOrWhiteSpace(Title))
        {
            Title = Path.GetFileNameWithoutExtension(path);
        }
        NotifyStartState();
        return true;
    }

    public void ClearDragOver() => IsDragOver = false;

    [RelayCommand]
    private void ClearFile()
    {
        ValidationError = null;
        ClearSelectedFile();
        NotifyStartState();
    }

    partial void OnSelectedFilePathChanged(string? value) => NotifyStartState();

    partial void OnIsStartingChanged(bool value) => NotifyStartState();

    partial void OnSelectedLanguageOptionChanged(LanguageOptionViewModel? value)
    {
        if (value is not null)
        {
            Language = value.Code;
        }
    }

    partial void OnSelectedQualityOptionChanged(QualityOptionViewModel? value)
    {
        if (value is not null)
        {
            Quality = value.Value;
        }
    }

    partial void OnSelectedSpeakerModeOptionChanged(SpeakerModeOptionViewModel? value)
    {
        if (value is not null)
        {
            SpeakerMode = value.Value;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartTranscriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFilePath) || string.IsNullOrWhiteSpace(Title))
        {
            return;
        }

        IsStarting = true;
        try
        {
            var transcriptionId = Guid.NewGuid();
            var mediaPath = await _mediaStorage.CopyToAppDataAsync(SelectedFilePath, transcriptionId);

            using var scope = _scopeFactory.CreateScope();
            var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
            var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var settings = await settingsService.GetAsync();

            await libraryService.CreateForUploadAsync(
                transcriptionId,
                Title.Trim(),
                mediaPath,
                Language,
                Quality,
                SpeakerMode,
                SelectedResearch?.Id,
                IconPicker.SelectedIcon,
                ParseTags(TagsText));

            _queueService.Enqueue(new TranscriptionJobRequest(
                transcriptionId,
                mediaPath,
                Language,
                Quality,
                settings.Device));

            await _services.GetRequiredService<SidebarViewModel>().LoadAsync();

            _navigation.NavigateTo(
                ScreenKey.Dashboard,
                new NavigationParameter(StatusFilter: LibraryStatusFilter.Progress));
        }
        finally
        {
            IsStarting = false;
        }
    }

    partial void OnTitleChanged(string value) => NotifyStartState();

    private async Task LoadFormAsync(int? preselectedResearchId)
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var researchService = scope.ServiceProvider.GetRequiredService<ResearchService>();

        var settings = await settingsService.GetAsync();
        Language = settings.DefaultLanguage;
        SpeakerMode = settings.IdentifySpeakersDefault ? SpeakerMode.Automatic : SpeakerMode.Off;
        SelectedLanguageOption = LanguageOptions.First(option => option.Code == Language);
        SelectedQualityOption = QualityOptions.First(option => option.Value == Quality);
        SelectedSpeakerModeOption = SpeakerModeOptions.First(option => option.Value == SpeakerMode);

        ResearchOptions.Clear();
        ResearchOptions.Add(new ResearchOptionViewModel { Id = null, Name = "Nenhuma (avulsa)" });

        foreach (var research in await researchService.GetAllAsync())
        {
            ResearchOptions.Add(new ResearchOptionViewModel
            {
                Id = research.Id,
                Name = research.Title,
            });
        }

        SelectedResearch = preselectedResearchId is int researchId
            ? ResearchOptions.FirstOrDefault(option => option.Id == researchId)
              ?? ResearchOptions[0]
            : ResearchOptions[0];
    }

    private void ClearSelectedFile()
    {
        SelectedFilePath = null;
        SelectedFileName = "";
        SelectedFileSize = "";
    }

    private void NotifyStartState()
    {
        OnPropertyChanged(nameof(HasSelectedFile));
        OnPropertyChanged(nameof(CanStart));
        StartTranscriptionCommand.NotifyCanExecuteChanged();
    }

    private static string FormatFileSize(long bytes)
    {
        var size = bytes / (1024d * 1024d);
        return size.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")) + " MB";
    }
    private static string[] ParseTags(string tagsText) =>
        tagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed record LanguageOptionViewModel(string Code, string Label)
{
    public override string ToString() => Label;
}

public sealed record QualityOptionViewModel(ModelQuality Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record SpeakerModeOptionViewModel(SpeakerMode Value, string Label)
{
    public override string ToString() => Label;
}
