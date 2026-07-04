using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using Transcriba.Core;
using Transcriba.Core.Data;
using Transcriba.Core.Engine;

namespace Transcriba.App;

class Program
{
    private static IHost? _host;

    public static IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Host ainda não foi inicializado.");

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        _host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                services.AddTranscribaDatabase();
                services.AddTranscribaEngine();
                services.AddTranscribaServices();
            })
            .Build();
        _host.Start();
        DbBootstrapper.MigrateAsync(_host.Services).GetAwaiter().GetResult();

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
