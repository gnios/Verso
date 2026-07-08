using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Verso.Core.Data;
using Verso.Core.Data.Entities;

namespace Verso.Tests.Data;

public class DbBootstrapperTests
{
    [Fact]
    public async Task AddVersoDatabase_CreatesDirectory_MigratesSchema_AndSharesDataAcrossContextsFromSameFactory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"transcriba-tests-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(directory, "verso.db");

        try
        {
            var services = new ServiceCollection();
            services.AddVersoDatabase(dbPath);

            Assert.True(Directory.Exists(directory));

            var provider = services.BuildServiceProvider();
            await DbBootstrapper.MigrateAsync(provider);

            Assert.True(File.Exists(dbPath));

            var factory = provider.GetRequiredService<IDbContextFactory<VersoDbContext>>();

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
        var dbPath = Path.Combine(directory, "verso.db");

        try
        {
            var services = new ServiceCollection();
            services.AddVersoDatabase(dbPath);
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IDbContextFactory<VersoDbContext>>();

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
