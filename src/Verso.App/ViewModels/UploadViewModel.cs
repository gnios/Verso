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
using Verso.App.Services;
using Verso.Core.Catalogs;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Services;

namespace Verso.App.ViewModels;

public partial class UploadViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IServiceProvider _services;
    private readonly NavigationService _navigation;
    private readonly MediaStorageService _mediaStorage;
    private readonly TranscriptionQueueService _queueService;

    public ObservableCollection<FolderOptionViewModel> FolderOptions { get; } = [];

    public IconPickerViewModel IconPicker { get; } = new();

    public bool HasIcon => !string.IsNullOrWhiteSpace(Icon);
    public string? Icon => IconPicker.SelectedIcon;

    [ObservableProperty]
    private bool _isIconPickerOpen;

    public IReadOnlyList<LanguageOptionViewModel> LanguageOptions { get; } =
    [
        new("pt", "Português (Brasil)"),
        new("es", "Español"),
        new("en", "English"),
    ];

    public IReadOnlyList<ModelOptionViewModel> ModelOptions { get; } = ModelCatalog.All;

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
    private ModelOptionViewModel? _selectedModelOption;

    [ObservableProperty]
    private SpeakerMode _speakerMode = SpeakerMode.Automatic;

    [ObservableProperty]
    private SpeakerModeOptionViewModel? _selectedSpeakerModeOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedFolderId))]
    private FolderOptionViewModel? _selectedFolder;

    /// <summary>Id da pasta selecionada — usado pelo FolderCombobox (mesmo padrão do Editor).</summary>
    public int? SelectedFolderId => SelectedFolder?.Id;

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
        IconPicker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IconPickerViewModel.SelectedIcon))
            {
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(HasIcon));
            }
        };
    }

    public void Initialize(NavigationParameter? parameter)
    {
        ClearSelectedFile();
        ValidationError = null;
        IsDragOver = false;
        IsStarting = false;
        Title = "";
        TagsText = "";
        IsIconPickerOpen = false;
        IconPicker.UseTranscriptionIcons = true;
        IconPicker.AllowNoIcon = true;
        IconPicker.SelectedIcon = IconCatalog.TransIcons[0];
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasIcon));
        _ = LoadFormAsync(parameter?.FolderId);
    }

    [RelayCommand]
    private void ToggleIconPicker() => IsIconPickerOpen = !IsIconPickerOpen;

    [RelayCommand]
    private void CloseIconPicker()
    {
        IsIconPickerOpen = false;
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasIcon));
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

    partial void OnSelectedLanguageOptionChanged(LanguageOptionViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        Language = value.Code;
    }

    partial void OnSelectedModelOptionChanged(ModelOptionViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        Quality = value.Value;
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
            var mediaPath = await _mediaStorage.CopyToStorageAsync(SelectedFilePath, transcriptionId);

            using var scope = _scopeFactory.CreateScope();
            var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
            var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var settings = await settingsService.GetAsync();
            var language = Language;
            var audioLoader = _services.GetRequiredService<Verso.Core.Engine.AudioLoader>();
            var durationSeconds = audioLoader.GetDuration(mediaPath);

            await libraryService.CreateForUploadAsync(
                transcriptionId,
                Title.Trim(),
                mediaPath,
                language,
                Quality,
                settings.Device,
                SpeakerMode,
                SelectedFolder?.Id,
                durationSeconds,
                IconPicker.SelectedIcon,
                ParseTags(TagsText));

            _queueService.Enqueue(new TranscriptionJobRequest(
                transcriptionId,
                mediaPath,
                language,
                Quality,
                settings.Device,
                settings.MaxTranscriptionThreads));

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

    private async Task LoadFormAsync(int? preselectedFolderId)
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();

        var settings = await settingsService.GetAsync();
        Language = settings.DefaultLanguage;
        SpeakerMode = settings.IdentifySpeakersDefault ? SpeakerMode.Automatic : SpeakerMode.Off;
        SelectedLanguageOption = LanguageOptions.First(option => option.Code == Language);
        Quality = settings.DefaultQuality;
        SelectedModelOption = ModelCatalog.Find(Quality);
        // Find dispara OnSelectedModelOptionChanged e normaliza Quality para o perfil de UI.
        SelectedSpeakerModeOption = SpeakerModeOptions.First(option => option.Value == SpeakerMode);

        FolderOptions.Clear();
        FolderOptions.Add(new FolderOptionViewModel { Id = null, Name = "Nenhuma pasta", Icon = "" });

        foreach (var folder in await folderService.GetAllAsync())
        {
            FolderOptions.Add(new FolderOptionViewModel
            {
                Id = folder.Id,
                Name = folder.Title,
                Icon = folder.Icon,
            });
        }

        SelectedFolder = preselectedFolderId is int folderId
            ? FolderOptions.FirstOrDefault(option => option.Id == folderId)
              ?? FolderOptions[0]
            : FolderOptions[0];

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

public sealed record SpeakerModeOptionViewModel(SpeakerMode Value, string Label)
{
    public override string ToString() => Label;
}
