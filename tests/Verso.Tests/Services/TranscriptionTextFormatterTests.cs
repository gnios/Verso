using Verso.Core.Export;

namespace Verso.Tests.Services;

public class TranscriptionTextFormatterTests
{
    [Fact]
    public void FormatTxtTimestamp_UnderOneHour_OmitsHours()
    {
        Assert.Equal("23:05", TranscriptionTextFormatter.FormatTxtTimestamp(23 * 60 + 5));
    }

    [Fact]
    public void FormatTxtTimestamp_OverOneHour_IncludesHours()
    {
        Assert.Equal("01:23:00", TranscriptionTextFormatter.FormatTxtTimestamp(83 * 60));
    }

    [Fact]
    public void SanitizeFileName_ReplacesInvalidCharacters()
    {
        var name = TranscriptionTextFormatter.SanitizeFileName("Reunião 17/08");

        Assert.DoesNotContain("/", name, StringComparison.Ordinal);
        Assert.Contains("Reunião", name, StringComparison.Ordinal);
        Assert.Contains("08", name, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeFileName_Empty_ReturnsFallback()
    {
        Assert.Equal("transcricao", TranscriptionTextFormatter.SanitizeFileName("   "));
    }
}

public class ExportPathResolverTests
{
    [Fact]
    public void CreateUniquePath_WhenFileExists_AppendsNumber()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"verso-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var first = ExportPathResolver.CreateUniquePath(dir, "entrevista", "txt");
            File.WriteAllText(first, "a");
            var second = ExportPathResolver.CreateUniquePath(dir, "entrevista", "txt");

            Assert.Equal(Path.Combine(dir, "entrevista.txt"), first);
            Assert.Equal(Path.Combine(dir, "entrevista (1).txt"), second);
            Assert.False(File.Exists(second));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
