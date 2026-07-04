using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.App.ViewModels;

namespace Transcriba.App.Services;

public partial class NavigationService : ObservableObject
{
    private readonly IServiceProvider _services;

    [ObservableProperty]
    private ScreenKey _currentScreen = ScreenKey.Dashboard;

    [ObservableProperty]
    private object? _currentViewModel;

    [ObservableProperty]
    private object? _navigationParameter;

    public NavigationService(IServiceProvider services)
    {
        _services = services;
        NavigateTo(ScreenKey.Dashboard);
    }

    public void NavigateTo(ScreenKey key, object? parameter = null)
    {
        CurrentScreen = key;
        NavigationParameter = parameter;
        CurrentViewModel = ResolveViewModel(key);
    }

    private object ResolveViewModel(ScreenKey key) => key switch
    {
        ScreenKey.Dashboard => _services.GetRequiredService<DashboardViewModel>(),
        ScreenKey.Research => _services.GetRequiredService<ResearchViewModel>(),
        ScreenKey.Upload => _services.GetRequiredService<UploadViewModel>(),
        ScreenKey.Recording => _services.GetRequiredService<RecordingViewModel>(),
        ScreenKey.Editor => _services.GetRequiredService<EditorViewModel>(),
        ScreenKey.Settings => _services.GetRequiredService<SettingsViewModel>(),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
    };
}
