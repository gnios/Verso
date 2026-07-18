using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Verso.Core.Logging;

/// <summary>
/// Logger de console minimalista, mesma linha única do <see cref="FileLoggerProvider"/> (hora,
/// nível, categoria curta, mensagem) — usado no lugar de
/// <c>Microsoft.Extensions.Logging.Console</c> (formato multi-linha "info: Namespace.Completo[0]"
/// + mensagem indentada na linha seguinte), que é difícil de acompanhar para quem não é dev.
/// Não escreve nada por conta própria fora do pipeline de <see cref="ILogger"/>: qualquer ruído
/// de bibliotecas de terceiros (ex.: tracing interno do Photino/WebView) que apareça no console de
/// debug não vem daqui — ver comentário em <c>Program.ConfigureLogging</c>.
/// </summary>
public sealed class SimpleConsoleLoggerProvider : ILoggerProvider
{
    private readonly SimpleConsoleLoggerOptions _options;
    private readonly object _lock = new();

    public SimpleConsoleLoggerProvider(IOptions<SimpleConsoleLoggerOptions>? options = null)
    {
        _options = options?.Value ?? new SimpleConsoleLoggerOptions();
    }

    public ILogger CreateLogger(string categoryName) => new SimpleConsoleLogger(categoryName, this);

    public void Dispose()
    {
    }

    internal void Log(string categoryName, LogLevel logLevel, string message, Exception? exception)
    {
        if (logLevel < _options.MinLevel)
        {
            return;
        }

        var line = VersoLogFormatter.FormatLine(categoryName, logLevel, message, exception);

        lock (_lock)
        {
            Console.WriteLine(line);
        }
    }
}

public sealed class SimpleConsoleLoggerOptions
{
    public LogLevel MinLevel { get; set; } = LogLevel.Information;
}

internal sealed class SimpleConsoleLogger(string categoryName, SimpleConsoleLoggerProvider provider) : ILogger
{
    IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;
    bool ILogger.IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Trace;

    void ILogger.Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        provider.Log(categoryName, logLevel, formatter(state, exception), exception);
    }
}
