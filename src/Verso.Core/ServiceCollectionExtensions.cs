using Microsoft.Extensions.DependencyInjection;
using Verso.Core.Export;
using Verso.Core.Services;

namespace Verso.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVersoServices(this IServiceCollection services)
    {
        services.AddScoped<LibraryService>();
        services.AddScoped<ResearchService>();
        services.AddSingleton<MediaStorageService>();
        services.AddScoped<SpeakerService>();
        services.AddSingleton<SegmentEditingService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ExportService>();
        // IMediaPlaybackService (NAudioPlaybackService Windows / HtmlMediaPlaybackService Linux)
        // é registrado no shell (Verso.App/AddVersoAppServices) — depende do SO e, no caso do
        // HTML5, de IJSRuntime que pertence à camada de UI.
        return services;
    }
}
