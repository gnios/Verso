using Verso.Core.Update;

namespace Verso.Tests.Update;

public class UpdateChannelTests
{
    [Fact]
    public void TryLoad_ReturnsNullWhenFileMissing()
    {
        var dir = CreateTempDir();
        try
        {
            Assert.Null(UpdateChannel.TryLoad(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TryLoad_ReturnsNullWhenJsonInvalid()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, UpdateChannel.FileName), "{ not json");
            Assert.Null(UpdateChannel.TryLoad(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TryLoad_ReturnsNullWhenVariantOrRidMissing()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, UpdateChannel.FileName), """{"variant":"gpu"}""");
            Assert.Null(UpdateChannel.TryLoad(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TryLoad_ReadsVariantAndRid()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, UpdateChannel.FileName),
                """{"variant":"gpu","rid":"win-x64"}""");

            var channel = UpdateChannel.TryLoad(dir);

            Assert.NotNull(channel);
            Assert.Equal("gpu", channel.Variant);
            Assert.Equal("win-x64", channel.Rid);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AssetFileName_MatchesReleaseZipConvention()
    {
        var channel = new UpdateChannel("cpu", "linux-x64");
        Assert.Equal("Verso-1.4.0-cpu-linux-x64.zip", channel.AssetFileName("1.4.0"));
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "verso-channel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
