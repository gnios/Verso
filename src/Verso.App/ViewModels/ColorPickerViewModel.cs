using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Verso.Core.Catalogs;

namespace Verso.App.ViewModels;

public partial class ColorPickerViewModel : ViewModelBase
{
    public ObservableCollection<ColorPickerItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private string _selectedColorName = ColorCatalog.PageColors[0].Name;

    public ColorPickerViewModel()
    {
        foreach (var color in ColorCatalog.PageColors)
        {
            Items.Add(new ColorPickerItemViewModel(this, color.Name, color.Hex));
        }

        UpdateSelectionStates();
    }

    partial void OnSelectedColorNameChanged(string value) => UpdateSelectionStates();

    internal void SelectColor(string name)
    {
        SelectedColorName = name;
    }

    private void UpdateSelectionStates()
    {
        foreach (var item in Items)
        {
            item.IsSelected = item.Name == SelectedColorName;
        }
    }
}
