using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Photino.Blazor;
using Verso.App.Components.Layout;
using Verso.App.Services;
using Verso.Core;
using Verso.Core.Data;
using Verso.Core.Engine;
using Verso.Core.Logging;
using Whisper.net.Logger;

namespace Verso.App;

public class Program
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("Serviços ainda não foram inicializados.");

    // Photino/WebView2 no Windows exigem STA. `async Task Main` quebra [STAThread]
    // (a continuação sai da thread STA) — entrada síncrona + GetAwaiter().GetResult().
    [STAThread]
    public static void Main(string[] args) =>
        MainAsync(args).GetAwaiter().GetResult();

    private static async Task MainAsync(string[] args)
    {
#if DEBUG
        AttachDebugConsole();
#endif

        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);
        appBuilder.RootComponents.Add<MainLayout>("#app");

        appBuilder.Services.AddLogging(ConfigureLogging);
        appBuilder.Services.AddVersoDatabase();
        appBuilder.Services.AddVersoEngine();
        appBuilder.Services.AddVersoServices();
        appBuilder.Services.AddVersoAppServices();

        var app = appBuilder.Build();
        _services = app.Services;
        app.Services.GetRequiredService<PhotinoWindowAccessor>().Attach(app.MainWindow);

        app.MainWindow
            .SetTitle("Verso")
            .SetSize(1280, 800)
            .Center()
            .RegisterCustomSchemeHandler(MediaSchemeHandler.Scheme, MediaSchemeHandler.Handle);
#if DEBUG
        app.MainWindow.SetDevToolsEnabled(true);
#endif

        AppDomain.CurrentDomain.UnhandledException += (_, error) =>
        {
            try
            {
                app.MainWindow.ShowMessage("Erro fatal", error.ExceptionObject?.ToString() ?? "(sem detalhes)");
            }
            catch
            {
                // Ignora falha ao mostrar diálogo se a janela já morreu.
            }
        };

        RouteWhisperNativeLogs(app.Services);
        UiThread.AttachLogger(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("UiThread"));
        await DbBootstrapper.MigrateAsync(app.Services);

        var hostedServices = app.Services.GetServices<IHostedService>().ToList();
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        try
        {
            app.Run();
        }
        finally
        {
            foreach (var hostedService in hostedServices)
            {
                await hostedService.StopAsync(CancellationToken.None);
            }
        }
    }

    // Logging unificado para debug e release: o logger em arquivo (rolling diário em
    // <appdir>/data/logs) é a única janela persistente do que ocorre por trás —
    // engine, fila de transcrição, downloads, erros nativos do whisper.cpp. O app é
    // portátil: logs ao lado do exe, não em %AppData%. O console só é anexado em #if
    // DEBUG (AttachDebugConsole abre uma janela de console a parte), usando o mesmo
    // formato de linha única do arquivo (AddVersoConsoleLogger) — NÃO o provider padrão
    // Microsoft.Extensions.Logging.Console (formato multi-linha "info: Namespace[0]").
    //
    // IMPORTANTE: não adicionar Trace.Listeners.Add(new ConsoleTraceListener(...)) aqui.
    // Isso já foi feito antes e capturou, além dos nossos logs, todo o tracing INTERNO do
    // Photino/Blazor (System.Diagnostics.Trace.WriteLine de coisas como
    // `Photino.NET: "Verso".SendWebMessage(__bwv:[...blob base64...])` a cada render do
    // WebView) — ruído irrelevante e ininteligível que afoga qualquer log real da
    // aplicação. Trace/Debug.WriteLine da nossa própria base (ver UiThread.cs) devem ir
    // para o ILogger normal, não para Trace.Listeners.
    private static void ConfigureLogging(ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddVersoFileLogger();
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        logging.AddFilter("Verso", LogLevel.Information);
        logging.AddFilter("Whisper.net", LogLevel.Warning);
        logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
#if DEBUG
        logging.AddVersoConsoleLogger();
#endif
    }

    // Encaminha os logs nativos do whisper.cpp (via whisper.net LogProvider) para o
    // logger em arquivo do app.
    private static void RouteWhisperNativeLogs(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        LogProvider.AddLogger((level, message) =>
        {
            logger.Log(MapWhisperLevel(level), "Whisper(native): {Message}", message);
        });
    }

    private static LogLevel MapWhisperLevel(WhisperLogLevel level) => level switch
    {
        WhisperLogLevel.Debug => LogLevel.Debug,
        WhisperLogLevel.Info => LogLevel.Debug,
        WhisperLogLevel.Warning => LogLevel.Warning,
        WhisperLogLevel.Error => LogLevel.Error,
        _ => LogLevel.Debug,
    };

#if DEBUG
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    private static void AttachDebugConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (AllocConsole())
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("Verso — console de debug");
        }
    }
#endif
}
