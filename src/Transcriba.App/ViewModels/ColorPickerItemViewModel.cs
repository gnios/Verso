using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Transcriba.App.ViewModels;

public partial class ColorPickerItemViewModel : ViewModelBase
{
    private readonly ColorPickerViewModel _parent;

    public string Name { get; }
    public string Hex { get; }

    [ObservableProperty]
    private bool _isSelected;

    public ColorPickerItemViewModel(ColorPickerViewModel parent, string name, string hex)
    {
        _parent = parent;
        Name = name;
        Hex = hex;
    }

    [RelayCommand]
    private void Select() => _parent.SelectColor(Name);
}
