using Microsoft.Extensions.DependencyInjection;
using Transcriba.App.Services;
using Transcriba.App.ViewModels;
using Transcriba.Core.Engine;

namespace Transcriba.App;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddTranscribaAppServices(this IServiceCollection services)
    {
        services.AddSingleton<NavigationService>();
        services.AddSingleton<BlazorThemeApplicator>();
        services.AddSingleton<IThemeApplicator>(sp => sp.GetRequiredService<BlazorThemeApplicator>());
        services.AddSingleton<IFileSaveService, WpfFileSaveService>();
        services.AddSingleton<IConfirmationService, WpfConfirmationService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<NewPageModalViewModel>();
        services.AddSingleton<ModelDownloadModalViewModel>();
        services.AddSingleton<IModelDownloadNotifier, ModelDownloadNotificationService>();
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
