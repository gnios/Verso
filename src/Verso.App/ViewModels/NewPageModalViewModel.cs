using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Verso.App.Services;
using Verso.Core.Catalogs;
using Verso.Core.Services;

namespace Verso.App.ViewModels;

/// <summary>
/// Modal "Nova pasta". A criação de transcrição avulsa (com arquivo, tags, ícone)
/// foi migrada para a tela <see cref="UploadViewModel"/>/<c>Upload.razor</c> — este modal
/// agora atende apenas a criação de pastas, então o modo (<see cref="NewPageMode"/>) e
/// todo o fluxo de seleção de arquivo/tags foram removidos (clean cutover).
/// </summary>
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
    private string _title = "";

    [ObservableProperty]
    private string? _validationError;

    [ObservableProperty]
    private bool _isConfirming;

    public bool CanConfirm => !string.IsNullOrWhiteSpace(Title) && !IsConfirming;

    public string ModalTitle => "Nova pasta";

    public string ConfirmButtonLabel => IsConfirming ? "Criando…" : "Criar";

    public string PreviewTitle =>
        string.IsNullOrWhiteSpace(Title) ? "Nova pasta" : Title;

    public string PreviewIcon => IconPicker.SelectedIcon ?? "📝";

    public string PreviewColorName => ColorPicker.SelectedColorName;

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

    public void Open()
    {
        Title = "";
        ValidationError = null;
        IsConfirming = false;
        IconPicker.UseTranscriptionIcons = false;
        IconPicker.AllowNoIcon = false;
        IconPicker.SelectedIcon = IconCatalog.PageIcons[0];
        ColorPicker.SelectedColorName = ColorCatalog.PageColors[0].Name;

        IsOpen = true;
        NotifyPreviewProperties();
        NotifyConfirmState();
        OnPropertyChanged(nameof(ModalTitle));
        OnPropertyChanged(nameof(ConfirmButtonLabel));
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        ValidationError = null;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            return;
        }

        IsConfirming = true;
        NotifyConfirmState();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
            await folderService.CreateAsync(
                Title.Trim(),
                IconPicker.SelectedIcon ?? IconCatalog.PageIcons[0],
                ColorPicker.SelectedColorName);

            IsOpen = false;
            await _services.GetRequiredService<SidebarViewModel>().LoadAsync();
            _navigation.NavigateTo(ScreenKey.Dashboard);
        }
        finally
        {
            IsConfirming = false;
            NotifyConfirmState();
        }
    }

    partial void OnTitleChanged(string value)
    {
        NotifyPreviewProperties();
        NotifyConfirmState();
    }

    partial void OnIsConfirmingChanged(bool value)
    {
        NotifyConfirmState();
        OnPropertyChanged(nameof(ConfirmButtonLabel));
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

    private void NotifyConfirmState()
    {
        OnPropertyChanged(nameof(CanConfirm));
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}