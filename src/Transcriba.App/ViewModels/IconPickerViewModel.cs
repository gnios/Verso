using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Transcriba.Core.Catalogs;

namespace Transcriba.App.ViewModels;

public partial class IconPickerViewModel : ViewModelBase
{
    public ObservableCollection<IconPickerItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private string? _selectedIcon = IconCatalog.PageIcons[0];

    [ObservableProperty]
    private bool _useTranscriptionIcons;

    [ObservableProperty]
    private bool _allowNoIcon;

    public IconPickerViewModel()
    {
        RebuildItems();
    }

    partial void OnUseTranscriptionIconsChanged(bool value) => RebuildItems();

    partial void OnAllowNoIconChanged(bool value) => RebuildItems();

    partial void OnSelectedIconChanged(string? value) => UpdateSelectionStates();

    internal void SelectIcon(string? icon)
    {
        SelectedIcon = icon;
    }

    internal void RebuildItems()
    {
        Items.Clear();

        if (AllowNoIcon)
        {
            Items.Add(new IconPickerItemViewModel(this, null, isNoIconOption: true));
        }

        var icons = UseTranscriptionIcons ? IconCatalog.TransIcons : IconCatalog.PageIcons;
        if (SelectedIcon is not null && !icons.Contains(SelectedIcon))
        {
            SelectedIcon = icons[0];
        }
        else if (SelectedIcon is null && !AllowNoIcon)
        {
            SelectedIcon = icons[0];
        }

        foreach (var icon in icons)
        {
            Items.Add(new IconPickerItemViewModel(this, icon));
        }

        UpdateSelectionStates();
    }

    private void UpdateSelectionStates()
    {
        foreach (var item in Items)
        {
            item.IsSelected = item.Icon == SelectedIcon;
        }
    }
}
