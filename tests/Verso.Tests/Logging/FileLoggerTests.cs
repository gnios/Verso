using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Verso.Core.Logging;

namespace Verso.Tests.Logging;

public class FileLoggerTests : IDisposable
{
    private readonly string _logDir = Path.Combine(Path.GetTempPath(), $"verso-logs-{Guid.NewGuid():N}");

    [Fact]
    public void WritesInformationLineToDailyFile()
    {
        var provider = CreateProvider(LogLevel.Information);

        var logger = provider.CreateLogger("Verso.Tests.Sample");
        logger.LogInformation("hello world");

        provider.Dispose();

        var file = Directory.GetFiles(_logDir).Single();
        var content = File.ReadAllText(file);
        Assert.Contains("hello world", content);
        Assert.Contains("[INF]", content);
        Assert.EndsWith(".log", file);
    }

    [Fact]
    public void FiltersBelowMinimumLevel()
    {
        var provider = CreateProvider(LogLevel.Information);

        var logger = provider.CreateLogger("Verso.Tests.Sample");
        logger.LogDebug("should-not-appear");

        provider.Dispose();

        Assert.False(Directory.Exists(_logDir) && Directory.GetFiles(_logDir).Any(),
            "Nenhum arquivo de log deve ser criado para logs abaixo do nível mínimo.");
    }

    [Fact]
    public void LogsErrorExceptionTypeAndMessage()
    {
        var provider = CreateProvider(LogLevel.Warning);

        var logger = provider.CreateLogger("Verso.Tests.Sample");
        logger.LogWarning(new InvalidOperationException("boom"), "algo falhou");

        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_logDir).Single());
        Assert.Contains("algo falhou", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("boom", content);
        Assert.Contains("[WRN]", content);
    }

    [Fact]
    public void RollsByDay_AppendsToSameFile()
    {
        var provider = CreateProvider(LogLevel.Information);

        var logger = provider.CreateLogger("Verso.Tests.Sample");
        logger.LogInformation("line 1");
        logger.LogInformation("line 2");

        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_logDir).Single());
        Assert.Contains("line 1", content);
        Assert.Contains("line 2", content);
    }

    [Fact]
    public void RegisterViaExtension_WritesToConfiguredDirectory()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b
            .ClearProviders()
            .AddVersoFileLogger(o => o.Directory = _logDir));
        using var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<FileLoggerTests>>();
        logger.LogInformation("via DI");
        // O provider é IDisposable hospedado; força flush descartando o container.
        sp.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_logDir).Single());
        Assert.Contains("via DI", content);
    }

    private FileLoggerProvider CreateProvider(LogLevel minLevel)
    {
        var options = Microsoft.Extensions.Options.Options.Create(
            new FileLoggerOptions { Directory = _logDir, MinLevel = minLevel });
        return new FileLoggerProvider(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_logDir))
        {
            Directory.Delete(_logDir, recursive: true);
        }
    }
}