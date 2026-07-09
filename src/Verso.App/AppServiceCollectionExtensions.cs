using Microsoft.Extensions.DependencyInjection;
using Verso.App.Services;
using Verso.App.ViewModels;
using Verso.Core.Engine;

namespace Verso.App;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddVersoAppServices(this IServiceCollection services)
    {
        services.AddSingleton<NavigationService>();
        services.AddSingleton<BlazorThemeApplicator>();
        services.AddSingleton<IThemeApplicator>(sp => sp.GetRequiredService<BlazorThemeApplicator>());
        services.AddSingleton<IFileSaveService, WpfFileSaveService>();
        services.AddSingleton<IFileOpenService, WpfFileOpenService>();
        services.AddSingleton<BlazorConfirmationService>();
        services.AddSingleton<IConfirmationService>(sp => sp.GetRequiredService<BlazorConfirmationService>());
        services.AddSingleton<ThemeService>();
        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<NewPageModalViewModel>();
        services.AddSingleton<ModelDownloadModalViewModel>();
        services.AddSingleton<IModelDownloadNotifier, ModelDownloadNotificationService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<GpuDetector>();
        services.AddSingleton<ActiveGpuResolver>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ResearchPageViewModel>();
        services.AddTransient<UploadViewModel>();
        services.AddTransient<RecordingViewModel>();
        services.AddTransient<EditorViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}
