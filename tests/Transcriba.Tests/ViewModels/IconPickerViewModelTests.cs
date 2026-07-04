using Transcriba.App.ViewModels;
using Transcriba.Core.Catalogs;

namespace Transcriba.Tests.ViewModels;

public class IconPickerViewModelTests
{
    [Fact]
    public void Default_UsesPageIcons()
    {
        var vm = new IconPickerViewModel();

        Assert.Equal(IconCatalog.PageIcons.Count, vm.Items.Count);
        Assert.Equal(IconCatalog.PageIcons[0], vm.SelectedIcon);
    }

    [Fact]
    public void SelectIcon_UpdatesSelectedIconAndSelectionState()
    {
        var vm = new IconPickerViewModel();
        var item = vm.Items.First(i => i.Icon == "🔬");

        item.SelectCommand.Execute(null);

        Assert.Equal("🔬", vm.SelectedIcon);
        Assert.True(item.IsSelected);
        Assert.All(vm.Items.Where(i => i.Icon != "🔬"), i => Assert.False(i.IsSelected));
    }

    [Fact]
    public void UseTranscriptionIcons_SwitchesToTransIconsCatalog()
    {
        var vm = new IconPickerViewModel { UseTranscriptionIcons = true };

        Assert.Equal(IconCatalog.TransIcons.Count, vm.Items.Count);
        Assert.Equal(IconCatalog.TransIcons[0], vm.SelectedIcon);
    }

    [Fact]
    public void AllowNoIcon_AddsNoIconOptionAndAllowsNullSelection()
    {
        var vm = new IconPickerViewModel { AllowNoIcon = true };

        Assert.Equal(IconCatalog.PageIcons.Count + 1, vm.Items.Count);
        var noIcon = Assert.Single(vm.Items, i => i.IsNoIconOption);

        noIcon.SelectCommand.Execute(null);

        Assert.Null(vm.SelectedIcon);
        Assert.True(noIcon.IsSelected);
    }
}
