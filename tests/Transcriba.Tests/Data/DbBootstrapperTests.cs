using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.Core.Data;
using Transcriba.Core.Data.Entities;

namespace Transcriba.Tests.Data;

public class DbBootstrapperTests
{
    [Fact]
    public async Task AddTranscribaDatabase_CreatesDirectory_MigratesSchema_AndSharesDataAcrossContextsFromSameFactory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"transcriba-tests-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(directory, "transcriba.db");

        try
        {
            var services = new ServiceCollection();
            services.AddTranscribaDatabase(dbPath);

            Assert.True(Directory.Exists(directory));

            var provider = services.BuildServiceProvider();
            await DbBootstrapper.MigrateAsync(provider);

            Assert.True(File.Exists(dbPath));

            var factory = provider.GetRequiredService<IDbContextFactory<TranscribaDbContext>>();

            await using (var writeContext = await factory.CreateDbContextAsync())
            {
                writeContext.Tags.Add(new Tag { Name = "mobilidade", ColorName = "blue" });
                await writeContext.SaveChangesAsync();
            }

            await using var readContext = await factory.CreateDbContextAsync();
            var persistedTag = await readContext.Tags.SingleAsync(t => t.Name == "mobilidade");

            Assert.Equal("blue", persistedTag.ColorName);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MigrateAsync_CalledAgainOnRestart_IsIdempotentAndKeepsExistingData()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"transcriba-tests-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(directory, "transcriba.db");

        try
        {
            var services = new ServiceCollection();
            services.AddTranscribaDatabase(dbPath);
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IDbContextFactory<TranscribaDbContext>>();

            await DbBootstrapper.MigrateAsync(provider);
            await using (var context = await factory.CreateDbContextAsync())
            {
                context.Tags.Add(new Tag { Name = "tese", ColorName = "pink" });
                await context.SaveChangesAsync();
            }

            // Simula reinício do app: mesmo caminho de banco, migration aplicada de novo.
            await DbBootstrapper.MigrateAsync(provider);

            await using var readContext = await factory.CreateDbContextAsync();
            var persistedTag = await readContext.Tags.SingleAsync(t => t.Name == "tese");

            Assert.Equal("pink", persistedTag.ColorName);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
