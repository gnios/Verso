using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.App.Services;
using Transcriba.Core.Catalogs;
using Transcriba.Core.Services;

namespace Transcriba.App.ViewModels;

public partial class NewPageModalViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IServiceProvider _services;
    private readonly NavigationService _navigation;

    public IconPickerViewModel IconPicker { get; } = new();
    public ColorPickerViewModel ColorPicker { get; } = new();

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private NewPageMode _mode = NewPageMode.Research;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _tagsText = "";

    public bool IsResearchMode => Mode == NewPageMode.Research;
    public bool IsTranscriptionMode => Mode == NewPageMode.Transcription;

    public string ModalTitle =>
        Mode == NewPageMode.Research ? "Nova pesquisa / tese" : "Nova transcrição avulsa";

    public string PreviewTitle =>
        string.IsNullOrWhiteSpace(Title)
            ? Mode == NewPageMode.Research ? "Nova pesquisa" : "Nova transcrição avulsa"
            : Title;

    public string PreviewIcon => IconPicker.SelectedIcon ?? "📝";

    public string PreviewColorName =>
        Mode == NewPageMode.Research ? ColorPicker.SelectedColorName : "blue";

    public bool IsBlue => PreviewColorName == "blue";
    public bool IsGreen => PreviewColorName == "green";
    public bool IsPurple => PreviewColorName == "purple";
    public bool IsOrange => PreviewColorName == "orange";
    public bool IsPink => PreviewColorName == "pink";
    public bool IsYellow => PreviewColorName == "yellow";
    public bool IsRed => PreviewColorName == "red";
    public bool IsTeal => PreviewColorName == "teal";

    public NewPageModalViewModel(
        IServiceScopeFactory scopeFactory,
        IServiceProvider services,
        NavigationService navigation)
    {
        _scopeFactory = scopeFactory;
        _services = services;
        _navigation = navigation;

        IconPicker.PropertyChanged += OnPickerPropertyChanged;
        ColorPicker.PropertyChanged += OnPickerPropertyChanged;
    }

    public void Open(NewPageMode mode)
    {
        Mode = mode;
        Title = "";
        TagsText = "";
        IconPicker.UseTranscriptionIcons = mode == NewPageMode.Transcription;
        IconPicker.AllowNoIcon = false;
        IconPicker.SelectedIcon = mode == NewPageMode.Transcription
            ? IconCatalog.TransIcons[0]
            : IconCatalog.PageIcons[0];
        ColorPicker.SelectedColorName = ColorCatalog.PageColors[0].Name;
        IsOpen = true;
        NotifyPreviewProperties();
        OnPropertyChanged(nameof(ModalTitle));
        OnPropertyChanged(nameof(IsResearchMode));
        OnPropertyChanged(nameof(IsTranscriptionMode));
    }

    [RelayCommand]
    private void Cancel() => IsOpen = false;

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();

        if (Mode == NewPageMode.Research)
        {
            var researchService = scope.ServiceProvider.GetRequiredService<ResearchService>();
            await researchService.CreateAsync(
                Title.Trim(),
                IconPicker.SelectedIcon ?? IconCatalog.PageIcons[0],
                ColorPicker.SelectedColorName);
        }
        else
        {
            var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
            var transcription = await libraryService.CreateStandaloneAsync(
                Title.Trim(),
                IconPicker.SelectedIcon,
                ParseTags(TagsText));

            IsOpen = false;
            await _services.GetRequiredService<SidebarViewModel>().LoadAsync();
            _navigation.NavigateTo(
                ScreenKey.Editor,
                new NavigationParameter(TranscriptionId: transcription.Id));
            return;
        }

        IsOpen = false;
        await _services.GetRequiredService<SidebarViewModel>().LoadAsync();
        _navigation.NavigateTo(ScreenKey.Dashboard);
    }

    partial void OnTitleChanged(string value) => NotifyPreviewProperties();

    partial void OnModeChanged(NewPageMode value)
    {
        OnPropertyChanged(nameof(ModalTitle));
        OnPropertyChanged(nameof(IsResearchMode));
        OnPropertyChanged(nameof(IsTranscriptionMode));
        NotifyPreviewProperties();
    }

    private void OnPickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IconPickerViewModel.SelectedIcon)
            or nameof(ColorPickerViewModel.SelectedColorName))
        {
            NotifyPreviewProperties();
        }
    }

    private void NotifyPreviewProperties()
    {
        OnPropertyChanged(nameof(PreviewTitle));
        OnPropertyChanged(nameof(PreviewIcon));
        OnPropertyChanged(nameof(PreviewColorName));
        OnPropertyChanged(nameof(IsBlue));
        OnPropertyChanged(nameof(IsGreen));
        OnPropertyChanged(nameof(IsPurple));
        OnPropertyChanged(nameof(IsOrange));
        OnPropertyChanged(nameof(IsPink));
        OnPropertyChanged(nameof(IsYellow));
        OnPropertyChanged(nameof(IsRed));
        OnPropertyChanged(nameof(IsTeal));
    }

    private static string[] ParseTags(string tagsText) =>
        tagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
