using Verso.Core.Data.Entities;
using Verso.App.ViewModels;

namespace Verso.Tests.ViewModels;

public class ModelCatalogTests
{
    [Fact]
    public void All_ExposesExactlyThreeProfilesInOrder()
    {
        Assert.Equal(3, ModelCatalog.All.Count);
        Assert.Equal(["Rápido", "Equilibrado", "Preciso"], ModelCatalog.All.Select(o => o.Label).ToArray());
        Assert.Equal(
            [ModelQuality.Base, ModelQuality.Standard, ModelQuality.LargeV3Turbo],
            ModelCatalog.All.Select(o => o.Value).ToArray());
    }

    [Fact]
    public void All_SpeedHintsAndWhenToUseAreFriendly()
    {
        Assert.Equal(
            ["Mais rápido", "Tempo médio", "Mais lento · maior qualidade"],
            ModelCatalog.All.Select(o => o.SpeedHint).ToArray());
        Assert.All(ModelCatalog.All, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.WhenToUse));
            Assert.DoesNotContain("Whisper", o.WhenToUse, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("large", o.WhenToUse, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ggml", o.WhenToUse, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(ModelQuality.Tiny, ModelQuality.Base)]
    [InlineData(ModelQuality.Base, ModelQuality.Base)]
    [InlineData(ModelQuality.TinyEn, ModelQuality.Base)]
    [InlineData(ModelQuality.Standard, ModelQuality.Standard)]
    [InlineData(ModelQuality.Medium, ModelQuality.Standard)]
    [InlineData(ModelQuality.SmallEn, ModelQuality.Standard)]
    [InlineData(ModelQuality.LargeV3Turbo, ModelQuality.LargeV3Turbo)]
    [InlineData(ModelQuality.High, ModelQuality.LargeV3Turbo)]
    [InlineData(ModelQuality.LargeV1, ModelQuality.LargeV3Turbo)]
    [InlineData(ModelQuality.LargeV2, ModelQuality.LargeV3Turbo)]
    public void ResolveProfile_MapsLegacyQualities(ModelQuality input, ModelQuality expected)
    {
        Assert.Equal(expected, ModelCatalog.ResolveProfile(input));
        Assert.Equal(expected, ModelCatalog.Find(input).Value);
    }

    [Fact]
    public void All_DescriptionsAreNonEmpty()
    {
        Assert.All(ModelCatalog.All, o => Assert.False(string.IsNullOrWhiteSpace(o.Description)));
    }
}
