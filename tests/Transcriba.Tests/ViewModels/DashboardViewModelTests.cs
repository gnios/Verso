using Microsoft.Extensions.DependencyInjection;
using Transcriba.App;
using Transcriba.App.Services;
using Transcriba.App.ViewModels;
using Transcriba.Core;
using Transcriba.Core.Data;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Services;
using Transcriba.Tests.Services;

namespace Transcriba.Tests.ViewModels;

public class DashboardViewModelTests
{
    private static async Task<(IServiceProvider Provider, string Directory, int TagId)> CreateDashboardProviderAsync()
    {
        var (baseProvider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "transcriba.db");

        var services = new ServiceCollection();
        services.AddTranscribaDatabase(dbPath);
        services.AddTranscribaServices();
        services.AddTranscribaAppServices();
        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);
        var tagId = await SeedDashboardDataAsync(provider);

        return (provider, directory, tagId);
    }

    private static async Task<int> SeedDashboardDataAsync(IServiceProvider provider)
    {
        var factory = TestDbHelper.GetFactory(provider);
        await using var ctx = await factory.CreateDbContextAsync();

        var tag = new Tag { Name = "mobilidade", ColorName = "blue" };
        ctx.Tags.Add(tag);

        var doneId = Guid.NewGuid();
        var progressId = Guid.NewGuid();
        var otherProgressId = Guid.NewGuid();

        ctx.Transcriptions.AddRange(
            new Transcription
            {
                Id = doneId,
                Title = "Entrevista concluída",
                Status = TranscriptionStatus.Done,
                DurationSeconds = 2520,
                CreatedAt = new DateTime(2025, 5, 3, 12, 0, 0, DateTimeKind.Utc),
                Segments =
                [
                    new Segment
                    {
                        Id = Guid.NewGuid(),
                        TranscriptionId = doneId,
                        StartSeconds = 0,
                        EndSeconds = 3,
                        Text = "Texto da entrevista finalizada",
                        SortOrder = 0
                    }
                ],
                Tags = [tag]
            },
            new Transcription
            {
                Id = progressId,
                Title = "Pesquisa em andamento",
                Status = TranscriptionStatus.InProgress,
                DurationSeconds = 1800,
                CreatedAt = new DateTime(2025, 4, 28, 12, 0, 0, DateTimeKind.Utc),
                Segments =
                [
                    new Segment
                    {
                        Id = Guid.NewGuid(),
                        TranscriptionId = progressId,
                        StartSeconds = 0,
                        EndSeconds = 3,
                        Text = "Falamos sobre mobilidade urbana e bicicleta",
                        SortOrder = 0
                    }
                ],
                Tags = [tag]
            },
            new Transcription
            {
                Id = otherProgressId,
                Title = "Entrevista em progresso",
                Status = TranscriptionStatus.InProgress,
                DurationSeconds = 900,
                CreatedAt = new DateTime(2025, 4, 15, 12, 0, 0, DateTimeKind.Utc),
                Segments =
                [
                    new Segment
                    {
                        Id = Guid.NewGuid(),
                        TranscriptionId = otherProgressId,
                        StartSeconds = 0,
                        EndSeconds = 3,
                        Text = "Conteúdo diferente",
                        SortOrder = 0
                    }
                ]
            });

        await ctx.SaveChangesAsync();
        return tag.Id;
    }

    private static async Task<DashboardViewModel> CreateDashboardAsync(IServiceProvider provider)
    {
        var navigation = provider.GetRequiredService<NavigationService>();
        navigation.NavigateTo(ScreenKey.Dashboard);
        var dashboard = Assert.IsType<DashboardViewModel>(navigation.CurrentViewModel);
        await Task.Delay(50);
        return dashboard;
    }

    [Fact]
    public async Task LoadAsync_AllFilter_ReturnsAllTranscriptions()
    {
        var (provider, directory, _) = await CreateDashboardProviderAsync();
        try
        {
            var dashboard = await CreateDashboardAsync(provider);

            Assert.Equal(3, dashboard.Cards.Count);
            Assert.False(dashboard.IsEmpty);
            Assert.True(dashboard.IsAllFilterActive);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SetProgressFilter_ReturnsOnlyInProgressCards()
    {
        var (provider, directory, _) = await CreateDashboardProviderAsync();
        try
        {
            var dashboard = await CreateDashboardAsync(provider);

            dashboard.SetProgressFilterCommand.Execute(null);
            await Task.Delay(50);

            Assert.Equal(2, dashboard.Cards.Count);
            Assert.All(dashboard.Cards, card => Assert.True(card.IsInProgress));
            Assert.True(dashboard.IsProgressFilterActive);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SetDoneFilter_ReturnsOnlyDoneCards()
    {
        var (provider, directory, _) = await CreateDashboardProviderAsync();
        try
        {
            var dashboard = await CreateDashboardAsync(provider);

            dashboard.SetDoneFilterCommand.Execute(null);
            await Task.Delay(50);

            Assert.Single(dashboard.Cards);
            Assert.Equal("Entrevista concluída", dashboard.Cards[0].Title);
            Assert.True(dashboard.Cards[0].IsDone);
            Assert.True(dashboard.IsDoneFilterActive);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task NavigationParameter_StatusFilter_IsAppliedOnLoad()
    {
        var (provider, directory, _) = await CreateDashboardProviderAsync();
        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.NavigateTo(
                ScreenKey.Dashboard,
                new NavigationParameter(StatusFilter: LibraryStatusFilter.Done));

            var dashboard = Assert.IsType<DashboardViewModel>(navigation.CurrentViewModel);
            await Task.Delay(50);

            Assert.Single(dashboard.Cards);
            Assert.True(dashboard.IsDoneFilterActive);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task NavigationParameter_TagFilter_ReturnsMatchingCards()
    {
        var (provider, directory, tagId) = await CreateDashboardProviderAsync();
        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.NavigateTo(
                ScreenKey.Dashboard,
                new NavigationParameter(TagId: tagId));

            var dashboard = Assert.IsType<DashboardViewModel>(navigation.CurrentViewModel);
            await Task.Delay(50);

            Assert.Equal(2, dashboard.Cards.Count);
            Assert.All(dashboard.Cards, card => Assert.Contains("mobilidade", card.Tags.Select(t => t.Name)));
            Assert.True(dashboard.IsAllFilterActive);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SearchText_WithProgressFilter_ReturnsMatchingCards()
    {
        var (provider, directory, _) = await CreateDashboardProviderAsync();
        try
        {
            var dashboard = await CreateDashboardAsync(provider);

            dashboard.SetProgressFilterCommand.Execute(null);
            dashboard.SearchText = "mobilidade";
            await Task.Delay(50);

            Assert.Single(dashboard.Cards);
            Assert.Equal("Pesquisa em andamento", dashboard.Cards[0].Title);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SearchText_WithNoMatches_ShowsEmptyState()
    {
        var (provider, directory, _) = await CreateDashboardProviderAsync();
        try
        {
            var dashboard = await CreateDashboardAsync(provider);

            dashboard.SearchText = "termo inexistente xyz";
            await Task.Delay(50);

            Assert.Empty(dashboard.Cards);
            Assert.True(dashboard.IsEmpty);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task OpenTranscription_NavigatesToEditorWithTranscriptionId()
    {
        var (provider, directory, _) = await CreateDashboardProviderAsync();
        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            var dashboard = await CreateDashboardAsync(provider);
            var transcriptionId = dashboard.Cards[0].Id;

            dashboard.Cards[0].OpenCommand.Execute(null);

            Assert.Equal(ScreenKey.Editor, navigation.CurrentScreen);
            var parameter = Assert.IsType<NavigationParameter>(navigation.NavigationParameter);
            Assert.Equal(transcriptionId, parameter.TranscriptionId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
