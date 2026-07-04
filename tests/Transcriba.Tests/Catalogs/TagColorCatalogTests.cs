using Transcriba.Core.Catalogs;

namespace Transcriba.Tests.Catalogs;

public class TagColorCatalogTests
{
    [Fact]
    public void GetColor_KnownTag_ReturnsMappedColor()
    {
        Assert.Equal("blue", TagColorCatalog.GetColor("mobilidade"));
    }

    [Fact]
    public void GetColor_UnknownTag_FallsBackToBlue()
    {
        Assert.Equal("blue", TagColorCatalog.GetColor("tag-desconhecida-xyz"));
    }
}
