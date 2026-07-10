using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Verso.App;
using Verso.App.Services;
using Verso.App.ViewModels;
using Verso.Core;
using Verso.Core.Data;
using Verso.Core.Engine;
using Verso.Tests.Engine;
using Verso.Tests.Services;

namespace Verso.Tests.ViewModels;

public class NewPageModalViewModelTests
{
    private static async Task<(IServiceProvider Provider, string Directory)> CreateProviderAsync()
    {
        var (_, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "verso.db");

        var services = new ServiceCollection();
        services.AddVersoDatabase(dbPath);
        services.AddVersoEngine();
        services.AddSingleton<ITranscriptionEngine>(new SuccessTranscriptionEngine());
        services.AddVersoServices();
        services.AddVersoAppServices();
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
    public async Task Cancel_DoesNotCreateFolder()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var modal = provider.GetRequiredService<NewPageModalViewModel>();

            modal.Open();
            modal.Title = "Pasta cancelada";
            modal.CancelCommand.Execute(null);

            Assert.False(modal.IsOpen);

            await using var context = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            Assert.Empty(await context.Folders.ToListAsync());
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ConfirmAsync_Folder_NavigatesToDashboardAndRefreshesSidebar()
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
            Assert.Contains(sidebar.Folders, r => r.Title == "Mobilidade urbana");
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}