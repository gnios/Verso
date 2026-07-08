using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Verso.Core.Services;

namespace Verso.App.Services;

public partial class ThemeService : ObservableObject
{
    private readonly IThemeApplicator _applicator;
    private readonly IServiceScopeFactory _scopeFactory;

    [ObservableProperty]
    private bool _isDark;

    public ThemeService(IThemeApplicator applicator, IServiceScopeFactory scopeFactory)
    {
        _applicator = applicator;
        _scopeFactory = scopeFactory;
    }

    public async Task InitializeAsync()
    {
        var settings = await WithSettingsService(s => s.GetAsync());
        IsDark = settings.DarkTheme;
        _applicator.Apply(IsDark);
    }

    public async Task ToggleAsync()
    {
        IsDark = !IsDark;
        _applicator.Apply(IsDark);
        await WithSettingsService(s => s.UpdateAsync(settings => settings.DarkTheme = IsDark));
    }

    private async Task WithSettingsService(Func<SettingsService, Task> action)
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        await action(settingsService);
    }

    private async Task<T> WithSettingsService<T>(Func<SettingsService, Task<T>> action)
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        return await action(settingsService);
    }
}
