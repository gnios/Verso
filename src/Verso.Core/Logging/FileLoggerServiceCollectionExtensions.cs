using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Verso.Core.Logging;

/// <summary>
/// Extensões de DI para o logger em arquivo do Verso.
/// </summary>
public static class FileLoggerServiceCollectionExtensions
{
    /// <summary>
    /// Adiciona o <see cref="FileLoggerProvider"/> ao pipeline de logging, escrevendo
    /// informativos e erros em arquivo rolling diário. Combinável com console (debug).
    /// </summary>
    public static ILoggingBuilder AddVersoFileLogger(
        this ILoggingBuilder builder,
        Action<FileLoggerOptions>? configure = null)
    {
        builder.Services.AddSingleton<FileLoggerProvider>();
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        builder.Services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<FileLoggerProvider>());
        return builder;
    }

    /// <summary>
    /// Adiciona um logger de console de linha única (mesmo formato do arquivo), no lugar do
    /// provider padrão <c>Microsoft.Extensions.Logging.Console</c>. Ver <see cref="SimpleConsoleLoggerProvider"/>.
    /// </summary>
    public static ILoggingBuilder AddVersoConsoleLogger(
        this ILoggingBuilder builder,
        Action<SimpleConsoleLoggerOptions>? configure = null)
    {
        builder.Services.AddSingleton<SimpleConsoleLoggerProvider>();
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        builder.Services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<SimpleConsoleLoggerProvider>());
        return builder;
    }
}