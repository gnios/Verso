using Verso.Core.Catalogs;

namespace Verso.Tests.Catalogs;

public class IconCatalogTests
{
    [Fact]
    public void PageIcons_HasExpectedCountAndIsNotEmpty()
    {
        Assert.NotEmpty(IconCatalog.PageIcons);
        Assert.Equal(30, IconCatalog.PageIcons.Count);
    }

    [Fact]
    public void TransIcons_HasExpectedCountAndIsNotEmpty()
    {
        Assert.NotEmpty(IconCatalog.TransIcons);
        Assert.Equal(18, IconCatalog.TransIcons.Count);
    }
}
