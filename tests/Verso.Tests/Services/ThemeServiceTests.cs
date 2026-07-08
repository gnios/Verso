using Microsoft.Extensions.DependencyInjection;
using Verso.Core.Services;

namespace Verso.Tests.Services;

public class ThemeServiceTests
{
    private sealed class FakeThemeApplicator : Verso.App.Services.IThemeApplicator
    {
        public bool LastApplied { get; private set; }

        public void Apply(bool darkTheme) => LastApplied = darkTheme;
    }

    private sealed class TestScopeFactory(SettingsService settingsService) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new TestScope(settingsService);
    }

    private sealed class TestScope(SettingsService settingsService) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new TestProvider(settingsService);

        public void Dispose()
        {
        }
    }

    private sealed class TestProvider(SettingsService settingsService) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(SettingsService) ? settingsService : null;
    }

    [Fact]
    public async Task ToggleAsync_AlternatesDarkThemeAndPersists()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var settingsService = new SettingsService(factory);
            var applicator = new FakeThemeApplicator();
            var themeService = new Verso.App.Services.ThemeService(
                applicator,
                new TestScopeFactory(settingsService));

            await themeService.InitializeAsync();
            Assert.False(themeService.IsDark);
            Assert.False(applicator.LastApplied);

            await themeService.ToggleAsync();
            Assert.True(themeService.IsDark);
            Assert.True(applicator.LastApplied);

            var settings = await settingsService.GetAsync();
            Assert.True(settings.DarkTheme);

            await themeService.ToggleAsync();
            Assert.False(themeService.IsDark);
            Assert.False(applicator.LastApplied);

            settings = await settingsService.GetAsync();
            Assert.False(settings.DarkTheme);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task InitializeAsync_LoadsPersistedDarkTheme()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var settingsService = new SettingsService(factory);
            await settingsService.UpdateAsync(s => s.DarkTheme = true);

            var applicator = new FakeThemeApplicator();
            var themeService = new Verso.App.Services.ThemeService(
                applicator,
                new TestScopeFactory(settingsService));

            await themeService.InitializeAsync();

            Assert.True(themeService.IsDark);
            Assert.True(applicator.LastApplied);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
