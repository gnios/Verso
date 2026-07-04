using Microsoft.Extensions.DependencyInjection;
using Transcriba.App.Services;
using Transcriba.App.ViewModels;

namespace Transcriba.App;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddTranscribaAppServices(this IServiceCollection services)
    {
        services.AddSingleton<NavigationService>();
        services.AddSingleton<IThemeApplicator, AvaloniaThemeApplicator>();
        services.AddSingleton<IFileSaveService, AvaloniaFileSaveService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<NewPageModalViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ResearchPageViewModel>();
        services.AddTransient<UploadViewModel>();
        services.AddTransient<RecordingViewModel>();
        services.AddTransient<EditorViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}
