using Verso.Core.Data.Entities;
using Verso.Core.Services;

namespace Verso.Tests.Services;

public class SettingsServiceTests
{
    [Fact]
    public async Task GetAsync_CreatesDefaultsWhenMissing()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var service = new SettingsService(factory);

            var settings = await service.GetAsync();

            Assert.Equal(1, settings.Id);
            Assert.Equal("pt", settings.DefaultLanguage);
            Assert.True(settings.IdentifySpeakersDefault);
            Assert.Equal(ExecutionDevice.Auto, settings.Device);
            Assert.False(settings.DarkTheme);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task UpdateAsync_PersistsPartialChangesWithoutOverwritingOtherFields()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var service = new SettingsService(factory);

            await service.UpdateAsync(s =>
            {
                s.Name = "Eugenia";
                s.DefaultLanguage = "en";
            });

            await service.UpdateAsync(s => s.DarkTheme = true);

            var settings = await service.GetAsync();
            Assert.Equal("Eugenia", settings.Name);
            Assert.Equal("en", settings.DefaultLanguage);
            Assert.True(settings.DarkTheme);
            Assert.True(settings.IdentifySpeakersDefault);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
