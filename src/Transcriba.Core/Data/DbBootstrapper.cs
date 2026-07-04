using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Transcriba.Core.Data;

public static class DbBootstrapper
{
    public static string GetDefaultDbPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Transcriba", "transcriba.db");

    /// <summary>
    /// Registra <see cref="IDbContextFactory{TranscribaDbContext}"/> no container de DI, apontando
    /// para <paramref name="dbPath"/> (ou o caminho padrão em %AppData% quando omitido), criando o
    /// diretório de destino se ainda não existir.
    /// </summary>
    public static IServiceCollection AddTranscribaDatabase(this IServiceCollection services, string? dbPath = null)
    {
        var path = dbPath ?? GetDefaultDbPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        services.AddDbContextFactory<TranscribaDbContext>(options => options.UseSqlite($"Data Source={path}"));
        return services;
    }

    /// <summary>Aplica as migrations pendentes na inicialização do app.</summary>
    public static async Task MigrateAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<TranscribaDbContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
