using Microsoft.Extensions.DependencyInjection;
using Verso.App;
using Verso.App.ViewModels;
using Verso.Core;
using Verso.Core.Data;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Services;
using Verso.Tests.Services;

namespace Verso.Tests.ViewModels;

public class SettingsViewModelTests
{
    private static async Task<(IServiceProvider Provider, string Directory)> CreateProviderAsync()
    {
        var (baseProvider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "verso.db");

        var services = new ServiceCollection();
        services.AddVersoDatabase(dbPath);
        services.AddVersoEngine();
        services.AddVersoServices();
        services.AddVersoAppServices();
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
                    DefaultQuality = ModelQuality.Medium,
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
            Assert.Equal(ModelQuality.Medium, settings.SelectedModelOption!.Value);
            Assert.NotNull(settings.RecommendedModelOption);
            Assert.False(string.IsNullOrEmpty(settings.ModelRecommendationReason));
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

    [Fact]
    public async Task ChangeModel_PersistsDefaultQuality()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var settings = CreateSettings(provider);
            await settings.LoadAsync();

            settings.SelectedModelOption = settings.ModelOptions.First(o => o.Value == ModelQuality.Tiny);
            await Task.Delay(50);

            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var saved = await service.GetAsync();

            Assert.Equal(ModelQuality.Tiny, saved.DefaultQuality);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ChangeDevice_UpdatesRecommendation()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var settings = CreateSettings(provider);
            await settings.LoadAsync();

            Assert.NotNull(settings.RecommendedModelOption);

            settings.SelectedDeviceOption = settings.DeviceOptions.First(o => o.Value == ExecutionDevice.Cuda);
            await Task.Delay(50);

            Assert.True(
                settings.RecommendedModelOption!.Value == ModelQuality.LargeV3Turbo ||
                settings.RecommendedModelOption!.Value == ModelQuality.High);

            settings.SelectedDeviceOption = settings.DeviceOptions.First(o => o.Value == ExecutionDevice.Cpu);
            await Task.Delay(50);

            Assert.NotNull(settings.RecommendedModelOption);

            Assert.True(settings.UseRecommendedModelCommand.CanExecute(null));
            settings.UseRecommendedModelCommand.Execute(null);
            Assert.Equal(settings.RecommendedModelOption!.Value, settings.SelectedModelOption!.Value);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task LoadAsync_PopulatesGpuList()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var settings = CreateSettings(provider);
            await settings.LoadAsync();

            // GpuDetector usa WMI; na pior hipótese (CI sem WMI) retorna lista vazia,
            // mas a propriedade nunca é null e o label de runtime existe.
            Assert.NotNull(settings.Gpus);
            Assert.False(string.IsNullOrEmpty(settings.RuntimePreferenceLabel));
            Assert.False(string.IsNullOrEmpty(settings.LogDirectoryPath));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task RefreshDeveloperInfoCommand_RepopulatesGpus()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var settings = CreateSettings(provider);
            await settings.LoadAsync();

            settings.RefreshDeveloperInfoCommand.Execute(null);

            Assert.NotNull(settings.Gpus);
            Assert.False(string.IsNullOrEmpty(settings.ConfiguredDeviceLabel));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task VerifyRuntimeCommand_WithoutModel_SetsMessageAndDoesNotThrow()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var settings = CreateSettings(provider);
            await settings.LoadAsync();

            // Aponta o modelo p/ um caminho inexistente (dir de testes isolado), o probe
            // devolve null sem lançar e a mensagem explica que não há modelo baixado.
            settings.VerifyRuntimeCommand.Execute(null);
            await Task.Delay(100);

            // Se já existe um runtime carregado (transcrição anterior no processo de
            // testes), a mensagem informa "já carregado"; caso contrário, informa que
            // não há modelo. Em ambos os casos DeveloperMessage é não-vazio OU o runtime
            // carregado está visível. Aceitamos qualquer caminho sem exceção.
            Assert.True(
                !string.IsNullOrEmpty(settings.DeveloperMessage) || settings.IsRuntimeLoaded,
                "VerifyRuntime deve produzir uma mensagem ou um runtime carregado.");
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
