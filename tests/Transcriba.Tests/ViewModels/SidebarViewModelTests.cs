using Microsoft.Extensions.DependencyInjection;
using Transcriba.App;
using Transcriba.App.Services;
using Transcriba.App.ViewModels;
using Transcriba.Core;
using Transcriba.Core.Data;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;
using Transcriba.Core.Services;
using Transcriba.Tests.Services;

namespace Transcriba.Tests.ViewModels;

public class SidebarViewModelTests
{
    private static async Task<(IServiceProvider Provider, string Directory)> CreateSidebarProviderAsync()
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

    [Fact]
    public async Task NavigateDashboard_SetsCurrentScreenToDashboard()
    {
        var (provider, directory) = await CreateSidebarProviderAsync();
        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            var sidebar = provider.GetRequiredService<SidebarViewModel>();

            sidebar.NavigateDashboardCommand.Execute(null);

            Assert.Equal(ScreenKey.Dashboard, navigation.CurrentScreen);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public void ToggleExpand_AlternatesResearchExpandedState()
    {
        var services = new ServiceCollection();
        services.AddTranscribaAppServices();
        var provider = services.BuildServiceProvider();
        var navigation = provider.GetRequiredService<NavigationService>();
        var research = new ResearchPage
        {
            Id = 1,
            Title = "Mobilidade urbana",
            Icon = "📚",
            ColorName = "blue"
        };
        var item = new SidebarResearchItemViewModel(research, navigation);

        Assert.False(item.IsExpanded);

        item.ToggleExpandCommand.Execute(null);
        Assert.True(item.IsExpanded);

        item.ToggleExpandCommand.Execute(null);
        Assert.False(item.IsExpanded);
    }

    [Fact]
    public async Task NavigateTag_NavigatesToDashboardWithTagFilter()
    {
        var (provider, directory) = await CreateSidebarProviderAsync();
        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            var tag = new SidebarTagItemViewModel(new TagSummary(7, "mobilidade", 3), navigation);

            tag.NavigateCommand.Execute(null);

            Assert.Equal(ScreenKey.Dashboard, navigation.CurrentScreen);
            var parameter = Assert.IsType<NavigationParameter>(navigation.NavigationParameter);
            Assert.Equal(7, parameter.TagId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task NavigateSettings_SetsCurrentScreenToSettings()
    {
        var (provider, directory) = await CreateSidebarProviderAsync();
        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            var sidebar = provider.GetRequiredService<SidebarViewModel>();

            sidebar.NavigateSettingsCommand.Execute(null);

            Assert.Equal(ScreenKey.Settings, navigation.CurrentScreen);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
