using Verso.App.ViewModels;
using Verso.Core.Catalogs;

namespace Verso.Tests.ViewModels;

public class ColorPickerViewModelTests
{
    [Fact]
    public void Default_HasEightColorsFromCatalog()
    {
        var vm = new ColorPickerViewModel();

        Assert.Equal(ColorCatalog.PageColors.Count, vm.Items.Count);
        Assert.Equal(ColorCatalog.PageColors[0].Name, vm.SelectedColorName);
    }

    [Fact]
    public void SelectColor_UpdatesSelectedColorAndSelectionState()
    {
        var vm = new ColorPickerViewModel();
        var item = vm.Items.First(i => i.Name == "green");

        item.SelectCommand.Execute(null);

        Assert.Equal("green", vm.SelectedColorName);
        Assert.True(item.IsSelected);
        Assert.All(vm.Items.Where(i => i.Name != "green"), i => Assert.False(i.IsSelected));
    }
}
