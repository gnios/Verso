using System.Text;

using Microsoft.Extensions.Logging;

namespace Verso.Core.Logging;

/// <summary>
/// Formatação de linha de log única e compartilhada entre <see cref="FileLoggerProvider"/> e
/// <see cref="SimpleConsoleLoggerProvider"/>: uma linha só por entrada (hora, nível curto,
/// categoria curta, mensagem), sem o formato multi-linha/verboso padrão do
/// <c>Microsoft.Extensions.Logging.Console</c> (que mistura "info:"/"warn:" com EventId e indenta
/// a mensagem numa segunda linha — confuso para quem não é dev). Console e arquivo mostram
/// exatamente a mesma coisa, então o que aparece na tela também fica registrado em disco.
/// </summary>
internal static class VersoLogFormatter
{
    public static string FormatLine(
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

    public static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    public static string ShortCategory(string categoryName)
    {
        var idx = categoryName.LastIndexOf('.');
        return idx >= 0 ? categoryName[(idx + 1)..] : categoryName;
    }
}
