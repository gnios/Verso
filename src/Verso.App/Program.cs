using System;
using System.Diagnostics;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verso.Core;
using Verso.Core.Data;
using Verso.Core.Engine;
using Verso.Core.Logging;
using Whisper.net.Logger;

namespace Verso.App;

public class Program
{
    private static IHost? _host;

    public static IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Host ainda não foi inicializado.");

    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        AttachDebugConsole();
#endif

        _host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(ConfigureLogging)
            .ConfigureServices(services =>
            {
                services.Configure<HostOptions>(options =>
                {
                    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
                });
                services.AddWpfBlazorWebView();
#if DEBUG
                services.AddBlazorWebViewDeveloperTools();
#endif
                services.AddVersoDatabase();
                services.AddVersoEngine();
                services.AddVersoServices();
                services.AddVersoAppServices();
            })
            .Build();
        RouteWhisperNativeLogs(_host.Services);
        DbBootstrapper.MigrateAsync(_host.Services).GetAwaiter().GetResult();
        _host.Start();

        try
        {
            var app = new App();
            // `InitializeComponent()` é gerado a partir do App.xaml e é quem aplica a
            // propriedade `StartupUri` (definida no XAML) à instância — sem essa chamada
            // o loop de mensagens do WPF roda (processo fica vivo) mas nenhuma janela é
            // criada, pois `StartupUri` nunca é atribuída. Normalmente essa chamada é feita
            // pelo `Main()` autogerado a partir de App.xaml, que não usamos aqui (Main
            // customizado em Program.cs, ver <StartupObject> no .csproj).
            app.InitializeComponent();
            app.Resources["Services"] = _host.Services;
            app.Run();
        }
        finally
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }
    }

    // Logging unificado para debug e release: o logger em arquivo (rolling diário em
    // <appdir>/data/logs) é a única janela persistente do que ocorre por trás —
    // engine, fila de transcrição, downloads, erros nativos do whisper.cpp. O app é
    // portátil: logs ao lado do exe, não em %AppData%. O console só é anexado em #if
    // DEBUG (AttachDebugConsole abre uma janela de console a parte).
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
        logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.None;
        });
        Trace.Listeners.Add(new ConsoleTraceListener(useErrorStream: false));
#endif
    }

    /// <summary>
    /// Encaminha os logs nativos do whisper.cpp (via whisper.net LogProvider) para o
    /// logger em arquivo do app. <see cref="LogProvider.AddLogger"/> recebe o nível
    /// nativo (WhisperLogLevel) e a mensagem; mapeamos para <see cref="LogLevel"/> e
    /// escrevemos sob a categoria "Whisper.net" para que apareçam no mesmo arquivo.
    /// </summary>
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
        if (AllocConsole())
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("Verso — console de debug");
        }
    }
#endif
}
