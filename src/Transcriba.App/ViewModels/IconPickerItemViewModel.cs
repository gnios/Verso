using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Transcriba.App.ViewModels;

public partial class IconPickerItemViewModel : ViewModelBase
{
    private readonly IconPickerViewModel _parent;

    public string? Icon { get; }
    public bool IsNoIconOption { get; }
    public string DisplayText => IsNoIconOption ? "—" : Icon!;

    [ObservableProperty]
    private bool _isSelected;

    public IconPickerItemViewModel(IconPickerViewModel parent, string? icon, bool isNoIconOption = false)
    {
        _parent = parent;
        Icon = icon;
        IsNoIconOption = isNoIconOption;
    }

    [RelayCommand]
    private void Select() => _parent.SelectIcon(Icon);
}
