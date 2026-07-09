using System;
using System.Linq;
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
        services.AddSingleton<IFileSaveService, PhotinoFileSaveService>();
        services.AddSingleton<IFileOpenService, PhotinoFileOpenService>();
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
        services.AddSingleton<FfmpegLocator>();

        // Playback de áudio: NAudio (WASAPI/Media Foundation) no Windows; HTML5 <audio>
        // via scheme customizado no Linux/macOS (NAudio não funciona fora do Windows).
        // Try-add: testes registram um fake entre AddVersoServices e AddVersoAppServices.
        if (!services.Any(d => d.ServiceType == typeof(Verso.Core.Media.IMediaPlaybackService)))
        {
            if (OperatingSystem.IsWindows())
                services.AddSingleton<Verso.Core.Media.IMediaPlaybackService, Verso.Core.Media.NAudioPlaybackService>();
            else
                services.AddSingleton<Verso.Core.Media.IMediaPlaybackService, Media.HtmlMediaPlaybackService>();
        }

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ResearchPageViewModel>();
        services.AddTransient<UploadViewModel>();
        services.AddTransient<RecordingViewModel>();
        services.AddTransient<EditorViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}
