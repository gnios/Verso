using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Photino.NET;
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
    public static void Main(string[] args)
    {
        using var sentry = InitializeSentry();
        try
        {
            MainAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Sentry.SentrySdk.CaptureException(ex);
            Sentry.SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            // WinExe não tem console — sem isto o app abre e fecha sem feedback.
            ShowStartupFailure(ex);
            Environment.Exit(1);
        }
    }

    private static IDisposable? InitializeSentry()
    {
        var dsn = Environment.GetEnvironmentVariable("SENTRY_DSN");
        if (string.IsNullOrWhiteSpace(dsn))
        {
            dsn = Verso.Core.SentryConfiguration.DesktopDsn;
        }

        // The worker is a child process and inherits this value.
        Environment.SetEnvironmentVariable("SENTRY_DSN", dsn);

        return Sentry.SentrySdk.Init(options =>
        {
            options.Dsn = dsn;
            options.Release = $"verso@{typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";
#if DEBUG
            options.Environment = "development";
#else
            options.Environment = "production";
#endif
            options.SendDefaultPii = false;
            options.SetBeforeSend((@event, _) =>
            {
                @event.Message = null!;
                @event.Request = null!;
                @event.User = null!;
                foreach (var exception in @event.SentryExceptions ?? Enumerable.Empty<Sentry.Protocol.SentryException>())
                {
                    exception.Value = "[redacted]";
                }
                return @event;
            });
            options.SetBeforeSendLog(static _ => null);
        });
    }

    private static async Task MainAsync(string[] args)
    {
#if DEBUG
        AttachDebugConsole();
#endif

        if (VersoPaths.IsPackagedWindowsApp)
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(VersoPaths.DataRoot, "WebView2"));
        }

        // Payload em engine/ (release): PATH + whisper LibraryPath antes de Photino/SQLite.
        NativePayloadBootstrap.Apply();

        var wwwroot = Path.Combine(VersoPaths.PayloadDirectory, "wwwroot");

        var services = new ServiceCollection();
        services.AddLogging(ConfigureLogging);
        services.AddVersoDatabase();
        services.AddVersoEngine(enableDiarization: false);
        services.AddVersoServices();
        services.AddVersoAppServices();

        var provider = services.BuildServiceProvider();
        _services = provider;

        UiThread.Attach();
        UiThread.AttachLogger(provider.GetRequiredService<ILoggerFactory>().CreateLogger("UiThread"));

        RouteWhisperNativeLogs(provider);
        await DbBootstrapper.MigrateAsync(provider);

        // HTTP local com Range — NÃO usar RegisterCustomSchemeHandler para áudio:
        // o Photino materializa o Stream inteiro em memória (IntPtr + numBytes).
        var mediaServer = provider.GetRequiredService<LocalMediaServer>();
        mediaServer.Start();

        // Serve o build React (wwwroot) para a janela Photino.
        var appServer = new LocalAppServer(
            provider.GetRequiredService<ILogger<LocalAppServer>>(),
            wwwroot);
        appServer.Start();

        // Limites da janela: abaixo de 1024x640 a tabela de transcrições +
        // sidebar ficam ilegíveis; acima de 1600x1000 a janela fica "esticada"
        // demais em monitores largos/HiDPI.
        var window = new PhotinoWindow()
            .SetTitle("Verso")
            .SetSize(1280, 800)
            .SetMinSize(1024, 640)
            .SetMaxSize(1600, 1000)
            .Center();

        // Ícone da janela (barra de título + taskbar): SetIconFile exige o HWND
        // nativo — chamar antes de WindowCreated crasha. O .ico vai pro output
        // via CopyToOutputDirectory no csproj.
        var windowIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "verso.ico");
        if (OperatingSystem.IsWindows() && File.Exists(windowIconPath))
        {
            window.RegisterWindowCreatedHandler((_, _) => window.SetIconFile(windowIconPath));
        }

#if DEBUG
        window.SetDevToolsEnabled(true);
#endif

        provider.GetRequiredService<PhotinoWindowAccessor>().Attach(window);

        var bridge = new NativeBridge(window, provider.GetRequiredService<ILogger<NativeBridge>>());
        provider.GetRequiredService<BridgeHost>().Attach(bridge);
        using var versoBridge = new VersoBridge(provider, bridge, provider.GetRequiredService<ILogger<VersoBridge>>());
        versoBridge.Attach();

        window.RegisterWebMessageReceivedHandler((sender, message) => bridge.HandleMessage(message));
        window.Load($"{appServer.BaseUrl}index.html");

        // Recupera de renderer morto (página em branco): o React envia app.ping;
        // silêncio prolongado recarrega a UI sem reiniciar o processo.
        using var watchdog = new WebViewWatchdog(
            window,
            bridge,
            $"{appServer.BaseUrl}index.html",
            provider.GetRequiredService<ILogger<WebViewWatchdog>>());

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        try
        {
            window.WaitForClose();
        }
        finally
        {
            foreach (var hostedService in hostedServices)
            {
                await hostedService.StopAsync(CancellationToken.None);
            }

            await mediaServer.DisposeAsync();
            await appServer.DisposeAsync();
        }
    }

    // Logging unificado para debug e release: o logger em arquivo (rolling diário em
    // pasta de dados do usuário) registra a fila de transcrição, downloads e erros
    // nativos do whisper.cpp. O console só é anexado em #if
    // DEBUG (AttachDebugConsole abre uma janela de console a parte), usando o mesmo
    // formato de linha única do arquivo (AddVersoConsoleLogger) — NÃO o provider padrão
    // Microsoft.Extensions.Logging.Console (formato multi-linha "info: Namespace[0]").
    private static void ConfigureLogging(ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddVersoFileLogger();
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SENTRY_DSN")))
        {
            logging.AddSentry(options =>
            {
                options.MinimumEventLevel = LogLevel.Error;
                options.MinimumBreadcrumbLevel = LogLevel.None;
            });
        }
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

    private static void ShowStartupFailure(Exception ex)
    {
        var detail = ex.ToString();
        var logPath = "(não foi possível gravar log — disco cheio ou sem permissão?)";
        try
        {
            var logDir = VersoPaths.LogsDirectory;
            Directory.CreateDirectory(logDir);
            logPath = Path.Combine(logDir, $"startup-crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(logPath, detail);
        }
        catch
        {
            // Disco cheio / pasta só leitura — ainda tenta mostrar o diálogo.
        }

        // MessageBox tem limite prático de tamanho; detalhe completo fica no arquivo.
        var summary =
            $"{ex.GetType().Name}: {ex.Message}{Environment.NewLine}{Environment.NewLine}" +
            $"Log completo:{Environment.NewLine}{logPath}";

        if (OperatingSystem.IsWindows())
        {
            MessageBoxW(IntPtr.Zero, summary, "Verso — falha ao iniciar", 0x00000010); // MB_ICONERROR
            return;
        }

        Console.Error.WriteLine(detail);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

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
