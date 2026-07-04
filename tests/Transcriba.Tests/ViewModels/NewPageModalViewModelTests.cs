using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.App;
using Transcriba.App.Services;
using Transcriba.App.ViewModels;
using Transcriba.Core;
using Transcriba.Core.Data;
using Transcriba.Core.Data.Entities;
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

            modal.Open(NewPageMode.Research);
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
    public async Task ConfirmAsync_StandaloneTranscription_NavigatesToEditor()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var modal = provider.GetRequiredService<NewPageModalViewModel>();
            var navigation = provider.GetRequiredService<NavigationService>();

            modal.Open(NewPageMode.Transcription);
            modal.Title = "Entrevista piloto";
            modal.TagsText = "campo, piloto";
            await modal.ConfirmCommand.ExecuteAsync(null);

            Assert.False(modal.IsOpen);
            Assert.Equal(ScreenKey.Editor, navigation.CurrentScreen);
            var parameter = Assert.IsType<NavigationParameter>(navigation.NavigationParameter);
            Assert.NotNull(parameter.TranscriptionId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ConfirmAsync_NewTags_UseBlueDefaultColor()
    {
        var (provider, directory) = await CreateProviderAsync();
        try
        {
            var modal = provider.GetRequiredService<NewPageModalViewModel>();

            modal.Open(NewPageMode.Transcription);
            modal.Title = "Notas de campo";
            modal.TagsText = "tag-nova-xyz";
            await modal.ConfirmCommand.ExecuteAsync(null);

            await using var context = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var tag = await context.Tags.SingleAsync(t => t.Name == "tag-nova-xyz");

            Assert.Equal("blue", tag.ColorName);
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

            modal.Open(NewPageMode.Research);
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

            modal.Open(NewPageMode.Research);
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
