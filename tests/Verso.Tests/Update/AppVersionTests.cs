using Verso.Core.Update;

namespace Verso.Tests.Update;

public class AppVersionTests
{
    [Theory]
    [InlineData("v1.2.4", "1.2.3", true)]
    [InlineData("1.2.4", "1.2.3", true)]
    [InlineData("v1.2.3", "1.2.3", false)]
    [InlineData("v1.2.3", "1.2.4", false)]
    [InlineData("v0.2.0", "0.1.9", true)]
    public void IsNewer_ComparesRemoteTagAgainstLocal(string remote, string local, bool expected)
    {
        Assert.Equal(expected, AppVersion.IsNewer(remote, local));
    }

    [Fact]
    public void IsNewer_TreatsInvalidLocalAsZero()
    {
        Assert.True(AppVersion.IsNewer("v1.0.0", "dev"));
        Assert.True(AppVersion.IsNewer("v0.1.0", ""));
        Assert.False(AppVersion.IsNewer("not-a-version", "1.0.0"));
    }

    [Fact]
    public void Parse_StripsVPrefixAndPrereleaseSuffix()
    {
        Assert.Equal(new Version(1, 2, 3), AppVersion.Parse("v1.2.3"));
        Assert.Equal(new Version(1, 2, 3), AppVersion.Parse("1.2.3-beta"));
        Assert.Equal(new Version(0, 0, 0), AppVersion.Parse("abc"));
    }
}
