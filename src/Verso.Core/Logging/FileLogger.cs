using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Verso.Core.Logging;

/// <summary>
/// Logger em arquivo rolling diário para o Verso. Escreve todas as categorias
/// (informativos e erros) em <c>&lt;appdir&gt;/data/logs/verso-AAAA-MM-DD.log</c>,
/// um arquivo por dia, rotacionando automaticamente quando a data muda. O app é portátil:
/// os logs ficam ao lado do executável, não em %AppData%.
///
/// É o único coletor de logs persistente do app: o console de debug (Program.cs)
/// só existe em #if DEBUG; em release, o arquivo é a única janela para o que
/// ocorre por trás — engine, fila de transcrição, downloads de modelo, erros.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLoggerOptions _options;
    private readonly object _lock = new();
    private string _currentDate = "";
    private StreamWriter? _writer;

    public FileLoggerProvider(IOptions<FileLoggerOptions>? options = null)
    {
        _options = options?.Value ?? new FileLoggerOptions();
        Directory.CreateDirectory(_options.Directory);
    }

    internal LogLevel MinLevel => _options.MinLevel;
    internal string DirectoryPath => _options.Directory;

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal void Log(
        string categoryName,
        LogLevel logLevel,
        string message,
        Exception? exception)
    {
        if (logLevel < _options.MinLevel)
        {
            return;
        }

        var line = FormatLine(categoryName, logLevel, message, exception);

        lock (_lock)
        {
            EnsureWriter();
            _writer?.WriteLine(line);
            _writer?.Flush();
        }
    }

    private static string FormatLine(
        string categoryName,
        LogLevel logLevel,
        string message,
        Exception? exception)
    {
        var sb = new StringBuilder(256);
        sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"));
        sb.Append(" [").Append(LevelTag(logLevel)).Append("] ");
        sb.Append('[').Append(ShortCategory(categoryName)).Append("] ");
        sb.Append(message);
        if (exception is not null)
        {
            sb.Append(" — ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
        }

        return sb.ToString();
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private static string ShortCategory(string categoryName)
    {
        var idx = categoryName.LastIndexOf('.');
        return idx >= 0 ? categoryName[(idx + 1)..] : categoryName;
    }

    private void EnsureWriter()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (_currentDate == today && _writer is not null)
        {
            return;
        }

        _writer?.Flush();
        _writer?.Dispose();

        _currentDate = today;
        var path = Path.Combine(_options.Directory, $"verso-{today}.log");
        _writer = new StreamWriter(path, append: true, Encoding.UTF8)
        {
            AutoFlush = false,
        };
    }
}

/// <summary>
/// Configuração do <see cref="FileLoggerProvider"/>. <see cref="Directory"/> é o
/// diretório de destino (padrão <c>&lt;appdir&gt;/data/logs</c>); <see cref="MinLevel"/>
/// é o nível mínimo (padrão <see cref="LogLevel.Information"/> — cobre informativos
/// e erros).
/// </summary>
public sealed class FileLoggerOptions
{
    public string Directory { get; set; } = VersoPaths.LogsDirectory;

    public LogLevel MinLevel { get; set; } = LogLevel.Information;
}

internal sealed class FileLogger(string categoryName, FileLoggerProvider provider) : ILogger
{
    IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;
    bool ILogger.IsEnabled(LogLevel logLevel) => logLevel >= provider.MinLevel;

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

internal sealed class NullScope : IDisposable
{
    public static readonly NullScope Instance = new();
    public void Dispose() { }
}