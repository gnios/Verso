using System;
using System.Diagnostics;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Transcriba.Core;
using Transcriba.Core.Data;
using Transcriba.Core.Engine;

namespace Transcriba.App;

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
            .ConfigureLogging(ConfigureDebugLogging)
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
                services.AddTranscribaDatabase();
                services.AddTranscribaEngine();
                services.AddTranscribaServices();
                services.AddTranscribaAppServices();
            })
            .Build();
        _host.Start();
        DbBootstrapper.MigrateAsync(_host.Services).GetAwaiter().GetResult();

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

#if DEBUG
    private static void ConfigureDebugLogging(ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.SetMinimumLevel(LogLevel.Debug);
        logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Information);
        logging.AddFilter("Transcriba", LogLevel.Debug);
        logging.AddConsole(options =>
        {
            // Erros iam para stderr; logs do EF Core iam para stdout — unifica no stdout.
            options.LogToStandardErrorThreshold = LogLevel.None;
        });
        Trace.Listeners.Add(new ConsoleTraceListener(useErrorStream: false));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    private static void AttachDebugConsole()
    {
        if (AllocConsole())
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("Transcriba — console de debug");
        }
    }
#else
    private static void ConfigureDebugLogging(ILoggingBuilder logging)
    {
    }
#endif
}
