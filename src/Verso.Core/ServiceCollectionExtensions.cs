using Microsoft.Extensions.DependencyInjection;
using Verso.Core.Export;
using Verso.Core.Services;

namespace Verso.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVersoServices(this IServiceCollection services)
    {
        services.AddScoped<LibraryService>();
        services.AddScoped<FolderService>();
        services.AddSingleton<MediaStorageService>();
        services.AddScoped<SpeakerService>();
        services.AddSingleton<SegmentEditingService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ExportService>();
        // IMediaPlaybackService é registrado no Verso.App (Html5AudioPlaybackService).
        return services;
    }
}
