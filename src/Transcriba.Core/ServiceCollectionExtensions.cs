using Microsoft.Extensions.DependencyInjection;
using Transcriba.Core.Export;
using Transcriba.Core.Services;

namespace Transcriba.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTranscribaServices(this IServiceCollection services)
    {
        services.AddScoped<LibraryService>();
        services.AddScoped<ResearchService>();
        services.AddSingleton<MediaStorageService>();
        services.AddScoped<SpeakerService>();
        services.AddSingleton<SegmentEditingService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ExportService>();
        return services;
    }
}
