using Microsoft.Extensions.DependencyInjection;
using Verso.App.Services;
using Verso.App.ViewModels;
using Verso.Core.Engine;
using System.Net.Http;
using Verso.Core.Services;

namespace Verso.App;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddVersoAppServices(this IServiceCollection services)
    {
        services.AddSingleton<NavigationService>();
        services.AddSingleton<BlazorThemeApplicator>();
        services.AddSingleton<IThemeApplicator>(sp => sp.GetRequiredService<BlazorThemeApplicator>());
        services.AddSingleton<PhotinoWindowAccessor>();
        services.AddSingleton<IFileSaveService, PhotinoFileSaveService>();
        services.AddSingleton<IFileOpenService, PhotinoFileOpenService>();
        services.AddSingleton<BlazorConfirmationService>();
        services.AddSingleton<IConfirmationService>(sp => sp.GetRequiredService<BlazorConfirmationService>());
        services.AddSingleton<ThemeService>();
        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<NewPageModalViewModel>();
        services.AddSingleton<ModelDownloadModalViewModel>();
        services.AddSingleton<FeedbackViewModel>();
        services.AddSingleton<FeedbackService>(_ => new(new HttpClient()));
        services.AddSingleton<IModelDownloadNotifier, ModelDownloadNotificationService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<GpuDetector>();
        services.AddSingleton<ActiveGpuResolver>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<FolderViewModel>();
        services.AddTransient<UploadViewModel>();
        services.AddTransient<RecordingViewModel>();
        services.AddTransient<EditorViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}
