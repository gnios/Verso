using Verso.Core.Catalogs;

namespace Verso.Tests.Catalogs;

public class ColorCatalogTests
{
    [Fact]
    public void PageColors_HasExpectedCount()
    {
        // NEWPAGE-01 AC1: seletor de cor com 8 cores.
        Assert.Equal(8, ColorCatalog.PageColors.Count);
    }
}
