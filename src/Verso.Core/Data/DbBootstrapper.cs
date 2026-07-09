using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Verso.Core.Data;

public static class DbBootstrapper
{
    public static string GetDefaultDbPath() => VersoPaths.DatabasePath;

    /// <summary>
    /// para <paramref name="dbPath"/> (ou o caminho portátil padrão em <c>&lt;appdir&gt;/data</c> quando omitido), criando o
    /// diretório de destino se ainda não existir.
    /// </summary>
    public static IServiceCollection AddVersoDatabase(this IServiceCollection services, string? dbPath = null)
    {
        var path = dbPath ?? GetDefaultDbPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        services.AddDbContextFactory<VersoDbContext>(options => options.UseSqlite($"Data Source={path}"));
        return services;
    }

    /// <summary>Aplica as migrations pendentes na inicialização do app.</summary>
    public static async Task MigrateAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<VersoDbContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
