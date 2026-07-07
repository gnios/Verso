using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.App;
using Transcriba.App.Services;
using Transcriba.App.ViewModels;
using Transcriba.Core;
using Transcriba.Core.Data;
using Transcriba.Core.Engine;
using Transcriba.Tests.Engine;
using Transcriba.Tests.Services;

namespace Transcriba.Tests.ViewModels;

public class NewPageModalViewModelTests
{
    private static async Task<(IServiceProvider Provider, string Directory)> CreateProviderAsync()
    {
        var (_, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "transcriba.db");

        var services = new ServiceCollection();
        services.AddTranscribaDatabase(dbPath);
        services.AddTranscribaEngine();
        services.AddSingleton<ITranscriptionEngine>(new SuccessTranscriptionEngine());
        services.AddTranscribaServices();
        services.AddTranscribaAppServices();
        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);

        return (provider, directory);
    }

    [Fact]
    public async Task ConfirmAsync_EmptyTitle_KeepsModalOpenAndDoesNotNavigate()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var modal = provider.GetRequiredService<NewPageModalViewModel>();
            var navigation = provider.GetRequiredService<NavigationService>();

            modal.Open();
            modal.Title = "   ";
            await modal.ConfirmCommand.ExecuteAsync(null);

            Assert.True(modal.IsOpen);
            Assert.Equal(ScreenKey.Dashboard, navigation.CurrentScreen);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task Cancel_DoesNotCreateResearch()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var modal = provider.GetRequiredService<NewPageModalViewModel>();

            modal.Open();
            modal.Title = "Pesquisa cancelada";
            modal.CancelCommand.Execute(null);

            Assert.False(modal.IsOpen);

            await using var context = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            Assert.Empty(await context.ResearchPages.ToListAsync());
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ConfirmAsync_Research_NavigatesToDashboardAndRefreshesSidebar()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var modal = provider.GetRequiredService<NewPageModalViewModel>();
            var navigation = provider.GetRequiredService<NavigationService>();
            var sidebar = provider.GetRequiredService<SidebarViewModel>();

            modal.Open();
            modal.Title = "Mobilidade urbana";
            modal.ColorPicker.SelectedColorName = "green";
            await modal.ConfirmCommand.ExecuteAsync(null);

            Assert.False(modal.IsOpen);
            Assert.Equal(ScreenKey.Dashboard, navigation.CurrentScreen);
            Assert.Contains(sidebar.Researches, r => r.Title == "Mobilidade urbana");
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}