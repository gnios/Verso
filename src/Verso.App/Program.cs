using System;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Photino.Blazor;
using Verso.App.Components.Layout;
using Verso.Core;
using Verso.Core.Data;
using Verso.Core.Engine;
using Verso.Core.Logging;
using Whisper.net.Logger;

namespace Verso.App;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        AttachDebugConsole();
#endif

        // wwwroot servido dos recursos embutidos no exe (ManifestEmbeddedFileProvider),
        // não de <output>/wwwroot no disco — a release não expõe mais a pasta wwwroot.
        var wwwrootProvider = new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot");
        var builder = PhotinoBlazorAppBuilder.CreateDefault(wwwrootProvider, args);

        builder.Services.AddLogging(ConfigureLogging);
        builder.Services.AddVersoDatabase();
        builder.Services.AddVersoEngine();
        builder.Services.AddVersoServices();
        builder.Services.AddVersoAppServices();

        builder.RootComponents.Add<MainLayout>("#app");

        var app = builder.Build();

        // Anexa a janela nativa ao UiThread (marshaling cross-platform via PhotinoWindow.Invoke).
        Services.UiThread.Initialize(app.MainWindow);

        // Scheme customizado que faz stream de arquivos de mídia locais para o <audio> HTML5
        // (HtmlMediaPlaybackService, Linux/macOS). O <audio> não abre file:// dentro do webview
        // isolado, então servimos os bytes do disco via verso-media://<caminho codificado>.
        app.MainWindow.RegisterCustomSchemeHandler("verso-media", HandleMediaScheme);

        RouteWhisperNativeLogs(app.Services);
        DbBootstrapper.MigrateAsync(app.Services).GetAwaiter().GetResult();

        app.MainWindow
            .SetTitle("Verso")
            .SetSize(1200, 800)
            .SetMinSize(900, 600)
            .SetIconFile("Assets/verso.ico");

        AppDomain.CurrentDomain.UnhandledException += (_, error) =>
            app.MainWindow.ShowMessage("Erro fatal", error.ExceptionObject?.ToString() ?? "(sem detalhes)");

        app.Run();
    }

    /// <summary>
    /// Handler do scheme <c>verso-media://</c>: faz stream do arquivo de mídia local
    /// (caminho absoluto codificado no segmento após o host) para o <c>&lt;audio&gt;</c> HTML5.
    /// URL esperada: <c>verso-media://localhost/&lt;Uri.EscapeDataString(path)&gt;</c>.
    /// </summary>
    private static System.IO.Stream? HandleMediaScheme(object sender, string scheme, string url, out string contentType)
    {
        contentType = "application/octet-stream";
        try
        {
            var uri = new Uri(url);
            // LocalPath começa com '/'; remove e decodata o caminho absoluto codificado.
            var encoded = uri.AbsolutePath.TrimStart('/');
            var path = Uri.UnescapeDataString(encoded);
            if (!System.IO.File.Exists(path))
                return null;

            contentType = MimeFromExtension(System.IO.Path.GetExtension(path));
            return System.IO.File.OpenRead(path);
        }
        catch
        {
            return null;
        }
    }

    private static string MimeFromExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".m4a" => "audio/mp4",
        ".mp4" => "audio/mp4",
        ".ogg" => "audio/ogg",
        ".webm" => "audio/webm",
        ".aac" => "audio/aac",
        _ => "application/octet-stream",
    };

    // Logging focado no que importa: início/conclusão da transcrição, runtime usado
    // (CUDA/Vulkan/CPU), progresso por parte e erros. O resto (Blazor/ASP.NET Core, EF
    // Core, logs nativos verbosos do whisper.cpp) fica em Warning+ — silencioso em
    // release. O console só é anexado em #if DEBUG.
    private static void ConfigureLogging(ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddVersoFileLogger();
        logging.SetMinimumLevel(LogLevel.Information);
        // Framework/infra: só avisos e erros (cala o ruído de Blazor/ASP.NET Core).
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("System", LogLevel.Warning);
        // Logs do próprio app (engine, fila, serviços) em Information — é o sinal que interessa.
        logging.AddFilter("Verso", LogLevel.Information);
#if DEBUG
        logging.SetMinimumLevel(LogLevel.Debug);
        logging.AddFilter("Verso", LogLevel.Debug);
        logging.AddConsole(options =>
        {
            // Erros iam para stderr; logs do EF Core iam para stdout — unifica no stdout.
            options.LogToStandardErrorThreshold = LogLevel.None;
        });
        Trace.Listeners.Add(new ConsoleTraceListener(useErrorStream: false));
#endif
    }

    /// <summary>
    /// Encaminha os logs nativos do whisper.cpp (via whisper.net LogProvider) para o
    /// logger em arquivo do app.
    /// </summary>
    private static void RouteWhisperNativeLogs(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        LogProvider.AddLogger((level, message) =>
        {
            logger.Log(MapWhisperLevel(level), "Whisper(native): {Message}", message);
        });
    }

    /// <summary>
    /// Mapeia os níveis de log nativos do whisper.cpp para os níveis do .NET.
    /// <c>Debug</c> e <c>Info</c> viram <c>LogLevel.Debug</c> — só aparecem em debug
    /// e não lotam o arquivo de log em release com centenas de linhas por segmento.
    /// <c>Warning</c> e <c>Error</c> do whisper.cpp sobem como tal em qualquer build.
    /// </summary>
    private static LogLevel MapWhisperLevel(WhisperLogLevel level) => level switch
    {
        WhisperLogLevel.Debug => LogLevel.Debug,
        WhisperLogLevel.Info => LogLevel.Debug,   // silencioso em release
        WhisperLogLevel.Warning => LogLevel.Warning,
        WhisperLogLevel.Error => LogLevel.Error,
        _ => LogLevel.Debug,
    };

#if DEBUG
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    private static void AttachDebugConsole()
    {
        if (AllocConsole())
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("Verso — console de debug");
        }
    }
#endif
}