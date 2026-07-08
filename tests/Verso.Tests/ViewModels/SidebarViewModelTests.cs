using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Verso.App;
using Verso.App.Services;
using Verso.App.ViewModels;
using Verso.Core;
using Verso.Core.Data;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Services;
using Verso.Tests.Services;

namespace Verso.Tests.ViewModels;

public class SidebarViewModelTests
{
    private static async Task<(IServiceProvider Provider, string Directory, int ResearchId, Guid TranscriptionId)>
        CreateSidebarProviderAsync(FakeConfirmationService? confirmation = null)
    {
        var (baseProvider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "verso.db");

        var services = new ServiceCollection();
        services.AddVersoDatabase(dbPath);
        services.AddVersoEngine();
        services.AddVersoServices();
        services.AddVersoAppServices();
        if (confirmation is not null)
        {
            services.AddSingleton<IConfirmationService>(confirmation);
        }

        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);

        var researchService = provider.GetRequiredService<ResearchService>();
        var research = await researchService.CreateAsync("Mobilidade urbana", "🚲", "green");
        var transcriptionId = Guid.NewGuid();
        await using (var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync())
        {
            ctx.Transcriptions.Add(new Transcription
            {
                Id = transcriptionId,
                Title = "Entrevista vinculada",
                Status = TranscriptionStatus.Done,
                ResearchPageId = research.Id,
            });
            await ctx.SaveChangesAsync();
        }

        return (provider, directory, research.Id, transcriptionId);
    }

    private static async Task<(IServiceProvider Provider, string Directory)> CreateSidebarProviderAsync()
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
        services.AddVersoAppServices();
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

        Assert.True(item.IsExpanded);

        item.ToggleExpandCommand.Execute(null);
        Assert.False(item.IsExpanded);

        item.ToggleExpandCommand.Execute(null);
        Assert.True(item.IsExpanded);
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
    public async Task NavigateUnassigned_NavigatesToDashboardWithUnassignedFilter()
    {
        var (provider, directory) = await CreateSidebarProviderAsync();
        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            var sidebar = provider.GetRequiredService<SidebarViewModel>();

            sidebar.NavigateUnassignedCommand.Execute(null);

            Assert.Equal(ScreenKey.Dashboard, navigation.CurrentScreen);
            var parameter = Assert.IsType<NavigationParameter>(navigation.NavigationParameter);
            Assert.True(parameter.UnassignedOnly);
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

    [Fact]
    public async Task DeleteResearch_Confirmed_DissociatesTranscriptionsAndRemovesFromSidebar()
    {
        var confirmation = new FakeConfirmationService { NextResult = true };
        var (provider, directory, researchId, transcriptionId) =
            await CreateSidebarProviderAsync(confirmation);

        try
        {
            var sidebar = provider.GetRequiredService<SidebarViewModel>();
            await sidebar.LoadAsync();

            await sidebar.DeleteResearchAsync(researchId);

            Assert.Contains("1 transcrição", confirmation.LastMessage ?? "");
            Assert.DoesNotContain(sidebar.Researches, item => item.Id == researchId);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            Assert.Null(await ctx.ResearchPages.FindAsync(researchId));
            var transcription = await ctx.Transcriptions.SingleAsync(t => t.Id == transcriptionId);
            Assert.Null(transcription.ResearchPageId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task DeleteResearch_Cancelled_KeepsResearch()
    {
        var confirmation = new FakeConfirmationService { NextResult = false };
        var (provider, directory, researchId, _) =
            await CreateSidebarProviderAsync(confirmation);

        try
        {
            var sidebar = provider.GetRequiredService<SidebarViewModel>();
            await sidebar.LoadAsync();

            await sidebar.DeleteResearchAsync(researchId);

            Assert.Contains(sidebar.Researches, item => item.Id == researchId);
            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            Assert.NotNull(await ctx.ResearchPages.FindAsync(researchId));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task DeleteResearch_FromOpenResearchPage_NavigatesToDashboard()
    {
        var confirmation = new FakeConfirmationService { NextResult = true };
        var (provider, directory, researchId, _) =
            await CreateSidebarProviderAsync(confirmation);

        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.NavigateTo(
                ScreenKey.Research,
                new NavigationParameter(ResearchId: researchId));
            var sidebar = provider.GetRequiredService<SidebarViewModel>();

            await sidebar.DeleteResearchAsync(researchId);

            Assert.Equal(ScreenKey.Dashboard, navigation.CurrentScreen);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
