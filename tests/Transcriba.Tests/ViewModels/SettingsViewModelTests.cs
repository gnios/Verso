using Microsoft.Extensions.DependencyInjection;
using Transcriba.App;
using Transcriba.App.ViewModels;
using Transcriba.Core;
using Transcriba.Core.Data;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;
using Transcriba.Core.Services;
using Transcriba.Tests.Services;

namespace Transcriba.Tests.ViewModels;

public class SettingsViewModelTests
{
    private static async Task<(IServiceProvider Provider, string Directory)> CreateProviderAsync()
    {
        var (baseProvider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "transcriba.db");

        var services = new ServiceCollection();
        services.AddTranscribaDatabase(dbPath);
        services.AddTranscribaEngine();
        services.AddTranscribaServices();
        services.AddTranscribaAppServices();
        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);

        return (provider, directory);
    }

    private static SettingsViewModel CreateSettings(IServiceProvider provider) =>
        provider.GetRequiredService<SettingsViewModel>();

    [Fact]
    public async Task LoadAsync_LoadsExistingSettings()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            await using (var ctx = await factory.CreateDbContextAsync())
            {
                ctx.UserSettings.Add(new UserSettings
                {
                    Id = 1,
                    Name = "Maria Santos",
                    Email = "maria.santos@usp.br",
                    Institution = "Universidade de São Paulo",
                    DefaultLanguage = "es",
                    IdentifySpeakersDefault = false,
                    LiveTranscriptionEnabled = false,
                    Device = ExecutionDevice.Vulkan,
                });
                await ctx.SaveChangesAsync();
            }

            var settings = CreateSettings(provider);
            await settings.LoadAsync();

            Assert.Equal("Maria Santos", settings.Name);
            Assert.Equal("maria.santos@usp.br", settings.Email);
            Assert.Equal("Universidade de São Paulo", settings.Institution);
            Assert.Equal("es", settings.SelectedLanguageOption!.Code);
            Assert.False(settings.IdentifySpeakersDefault);
            Assert.False(settings.LiveTranscriptionEnabled);
            Assert.Equal(ExecutionDevice.Vulkan, settings.SelectedDeviceOption!.Value);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SaveProfile_PersistsNameEmailInstitution()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var settings = CreateSettings(provider);
            await settings.LoadAsync();

            settings.Name = "Ana Costa";
            settings.Email = "ana@unicamp.br";
            settings.Institution = "UNICAMP";
            await settings.SaveProfileCommand.ExecuteAsync(null);

            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var saved = await service.GetAsync();

            Assert.Equal("Ana Costa", saved.Name);
            Assert.Equal("ana@unicamp.br", saved.Email);
            Assert.Equal("UNICAMP", saved.Institution);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ChangeLanguage_PersistsDefaultLanguage()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var settings = CreateSettings(provider);
            await settings.LoadAsync();

            settings.SelectedLanguageOption = settings.LanguageOptions.First(option => option.Code == "en");
            await Task.Delay(50);

            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var saved = await service.GetAsync();

            Assert.Equal("en", saved.DefaultLanguage);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ToggleIdentifySpeakers_PersistsPreference()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var settings = CreateSettings(provider);
            await settings.LoadAsync();

            settings.IdentifySpeakersDefault = false;
            await Task.Delay(50);

            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var saved = await service.GetAsync();

            Assert.False(saved.IdentifySpeakersDefault);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ToggleLiveTranscription_PersistsPreference()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var settings = CreateSettings(provider);
            await settings.LoadAsync();

            settings.LiveTranscriptionEnabled = false;
            await Task.Delay(50);

            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var saved = await service.GetAsync();

            Assert.False(saved.LiveTranscriptionEnabled);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ChangeDevice_PersistsExecutionDevice()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var settings = CreateSettings(provider);
            await settings.LoadAsync();

            settings.SelectedDeviceOption = settings.DeviceOptions.First(option => option.Value == ExecutionDevice.Cuda);
            await Task.Delay(50);

            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var saved = await service.GetAsync();

            Assert.Equal(ExecutionDevice.Cuda, saved.Device);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
