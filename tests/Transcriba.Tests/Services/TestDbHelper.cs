using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.Core.Data;

namespace Transcriba.Tests.Services;

internal static class TestDbHelper
{
    public static async Task<(IServiceProvider Provider, string Directory)> CreateIsolatedDatabaseAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"transcriba-tests-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(directory, "transcriba.db");

        var services = new ServiceCollection();
        services.AddTranscribaDatabase(dbPath);
        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);

        return (provider, directory);
    }

    public static IDbContextFactory<TranscribaDbContext> GetFactory(IServiceProvider provider) =>
        provider.GetRequiredService<IDbContextFactory<TranscribaDbContext>>();

    public static void Cleanup(string directory)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
