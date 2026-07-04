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

public class ResearchPageViewModelTests
{
    private static async Task<(IServiceProvider Provider, string Directory, int ResearchId, Guid DoneId, Guid ProgressId)>
        CreateResearchProviderAsync()
    {
        var (baseProvider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "transcriba.db");

        var services = new ServiceCollection();
        services.AddTranscribaDatabase(dbPath);
        services.AddTranscribaServices();
        services.AddTranscribaAppServices();
        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);

        var researchService = provider.GetRequiredService<ResearchService>();
        var research = await researchService.CreateAsync(
            "Mobilidade urbana",
            "🚲",
            "green");
        research.Description = "Pesquisa sobre transporte sustentável";
        await using (var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync())
        {
            var page = await ctx.ResearchPages.FindAsync(research.Id);
            page!.Description = research.Description;
            await ctx.SaveChangesAsync();
        }

        var doneId = Guid.NewGuid();
        var progressId = Guid.NewGuid();
        await using (var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync())
        {
            ctx.Transcriptions.AddRange(
                new Transcription
                {
                    Id = doneId,
                    Title = "Entrevista concluída",
                    Icon = "🎤",
                    Status = TranscriptionStatus.Done,
                    DurationSeconds = 2520,
                    ResearchPageId = research.Id,
                    CreatedAt = new DateTime(2025, 5, 3, 12, 0, 0, DateTimeKind.Utc),
                    Segments =
                    [
                        new Segment
                        {
                            Id = Guid.NewGuid(),
                            TranscriptionId = doneId,
                            StartSeconds = 0,
                            EndSeconds = 3,
                            Text = "Texto concluído",
                            SortOrder = 0
                        }
                    ]
                },
                new Transcription
                {
                    Id = progressId,
                    Title = "Entrevista em andamento",
                    Icon = "📝",
                    Status = TranscriptionStatus.InProgress,
                    DurationSeconds = 900,
                    ResearchPageId = research.Id,
                    CreatedAt = new DateTime(2025, 4, 28, 12, 0, 0, DateTimeKind.Utc),
                    Segments =
                    [
                        new Segment
                        {
                            Id = Guid.NewGuid(),
                            TranscriptionId = progressId,
                            StartSeconds = 0,
                            EndSeconds = 3,
                            Text = "Texto em progresso",
                            SortOrder = 0
                        }
                    ]
                });

            await ctx.SaveChangesAsync();
        }

        return (provider, directory, research.Id, doneId, progressId);
    }

    private static async Task<ResearchPageViewModel> CreateResearchPageAsync(
        IServiceProvider provider,
        int researchId)
    {
        var navigation = provider.GetRequiredService<NavigationService>();
        navigation.NavigateTo(
            ScreenKey.Research,
            new NavigationParameter(ResearchId: researchId));

        var page = Assert.IsType<ResearchPageViewModel>(navigation.CurrentViewModel);
        await Task.Delay(50);
        return page;
    }

    [Fact]
    public async Task NavigationParameter_ResearchId_LoadsHeaderAndTranscriptions()
    {
        var (provider, directory, researchId, _, _) = await CreateResearchProviderAsync();
        try
        {
            var page = await CreateResearchPageAsync(provider, researchId);

            Assert.Equal("Mobilidade urbana", page.Title);
            Assert.Equal("Pesquisa sobre transporte sustentável", page.Description);
            Assert.Equal("🚲", page.Icon);
            Assert.True(page.IsGreen);
            Assert.Equal(2, page.Transcriptions.Count);
            Assert.False(page.IsEmpty);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task LoadAsync_EmptyTranscriptions_ShowsEmptyStateWithoutError()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var dbPath = Path.Combine(directory, "transcriba.db");
            var services = new ServiceCollection();
            services.AddTranscribaDatabase(dbPath);
            services.AddTranscribaServices();
            services.AddTranscribaAppServices();
            var scopedProvider = services.BuildServiceProvider();
            await DbBootstrapper.MigrateAsync(scopedProvider);

            var researchService = scopedProvider.GetRequiredService<ResearchService>();
            var research = await researchService.CreateAsync("Pesquisa vazia", "📚", "blue");

            var page = await CreateResearchPageAsync(scopedProvider, research.Id);

            Assert.Empty(page.Transcriptions);
            Assert.True(page.IsEmpty);
            Assert.Equal("Pesquisa vazia", page.Title);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task AddTranscription_NavigatesToUploadWithResearchId()
    {
        var (provider, directory, researchId, _, _) = await CreateResearchProviderAsync();
        try
        {
            var page = await CreateResearchPageAsync(provider, researchId);
            var navigation = provider.GetRequiredService<NavigationService>();

            page.AddTranscriptionCommand.Execute(null);

            Assert.Equal(ScreenKey.Upload, navigation.CurrentScreen);
            var parameter = Assert.IsType<NavigationParameter>(navigation.NavigationParameter);
            Assert.Equal(researchId, parameter.ResearchId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task OpenTranscription_NavigatesToEditorWithTranscriptionId()
    {
        var (provider, directory, researchId, doneId, _) = await CreateResearchProviderAsync();
        try
        {
            var page = await CreateResearchPageAsync(provider, researchId);
            var navigation = provider.GetRequiredService<NavigationService>();

            page.Transcriptions[0].OpenCommand.Execute(null);

            Assert.Equal(ScreenKey.Editor, navigation.CurrentScreen);
            var parameter = Assert.IsType<NavigationParameter>(navigation.NavigationParameter);
            Assert.Equal(doneId, parameter.TranscriptionId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task NavigateDashboard_NavigatesToDashboard()
    {
        var (provider, directory, researchId, _, _) = await CreateResearchProviderAsync();
        try
        {
            var page = await CreateResearchPageAsync(provider, researchId);
            var navigation = provider.GetRequiredService<NavigationService>();

            page.NavigateDashboardCommand.Execute(null);

            Assert.Equal(ScreenKey.Dashboard, navigation.CurrentScreen);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
