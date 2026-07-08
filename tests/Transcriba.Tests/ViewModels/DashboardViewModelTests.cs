using System.Linq;
using Microsoft.EntityFrameworkCore;
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

public class DashboardViewModelTests
{
    private static async Task<(IServiceProvider Provider, string Directory, int TagId, Guid ErrorId, Guid DoneId)>
        CreateDashboardProviderAsync(FakeConfirmationService? confirmation = null)
    {
        var (baseProvider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "transcriba.db");

        var services = new ServiceCollection();
        services.AddTranscribaDatabase(dbPath);
        services.AddTranscribaEngine();
        services.AddTranscribaServices();
        services.AddTranscribaAppServices();
        services.AddSingleton(new MediaStorageService(Path.Combine(directory, "media")));
        if (confirmation is not null)
        {
            services.AddSingleton<IConfirmationService>(confirmation);
        }

        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);
        var (tagId, errorId, doneId) = await SeedDashboardDataAsync(provider, directory);

        return (provider, directory, tagId, errorId, doneId);
    }

    private static async Task<(int TagId, Guid ErrorId, Guid DoneId)> SeedDashboardDataAsync(
        IServiceProvider provider,
        string directory)
    {
        var factory = TestDbHelper.GetFactory(provider);
        await using var ctx = await factory.CreateDbContextAsync();

        var tag = new Tag { Name = "mobilidade", ColorName = "blue" };
        ctx.Tags.Add(tag);

        var doneId = Guid.NewGuid();
        var progressId = Guid.NewGuid();
        var otherProgressId = Guid.NewGuid();
        var errorId = Guid.NewGuid();

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
            },
            new Transcription
            {
                Id = errorId,
                Title = "Falha na transcrição",
                Status = TranscriptionStatus.Error,
                ErrorMessage = "falha simulada",
                MediaFilePath = Path.Combine(directory, "media.wav"),
                Language = "pt",
                Quality = ModelQuality.Standard,
                CreatedAt = new DateTime(2025, 4, 10, 12, 0, 0, DateTimeKind.Utc),
            });

        await ctx.SaveChangesAsync();
        await File.WriteAllTextAsync(Path.Combine(directory, "media.wav"), "wav");
        return (tag.Id, errorId, doneId);
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
        var (provider, directory, _, _, _) = await CreateDashboardProviderAsync();
        try
        {
            var dashboard = await CreateDashboardAsync(provider);

            Assert.Equal(4, dashboard.Cards.Count);
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
        var (provider, directory, _, _, _) = await CreateDashboardProviderAsync();
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
        var (provider, directory, _, _, _) = await CreateDashboardProviderAsync();
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
        var (provider, directory, _, _, _) = await CreateDashboardProviderAsync();
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
        var (provider, directory, tagId, _, _) = await CreateDashboardProviderAsync();
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
    public async Task NavigationParameter_UnassignedOnly_ActivatesFilterAndReturnsAvulsaCards()
    {
        var (provider, directory, _, _, _) = await CreateDashboardProviderAsync();
        try
        {
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.NavigateTo(
                ScreenKey.Dashboard,
                new NavigationParameter(UnassignedOnly: true));

            var dashboard = Assert.IsType<DashboardViewModel>(navigation.CurrentViewModel);
            await Task.Delay(50);

            Assert.True(dashboard.IsUnassignedFilterActive);
            // A semente do dashboard não associa nenhuma transcrição a uma pesquisa — todas são avulsas.
            Assert.Equal(4, dashboard.Cards.Count);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SearchText_WithProgressFilter_ReturnsMatchingCards()
    {
        var (provider, directory, _, _, _) = await CreateDashboardProviderAsync();
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
        var (provider, directory, _, _, _) = await CreateDashboardProviderAsync();
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
        var (provider, directory, _, _, _) = await CreateDashboardProviderAsync();
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

    [Fact]
    public async Task StatusChanged_UpdatesExistingCardToDone()
    {
        var (provider, directory, _, _, _) = await CreateDashboardProviderAsync();
        try
        {
            var dashboard = await CreateDashboardAsync(provider);
            var queue = provider.GetRequiredService<TranscriptionQueueService>();
            var progressCard = dashboard.Cards.First(card => card.IsInProgress);

            RaiseStatusChanged(queue, progressCard.Id, TranscriptionStatusChanged.Done);

            Assert.True(progressCard.IsDone);
            Assert.Null(progressCard.ErrorMessage);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task StatusChanged_Error_SetsErrorMessageAndRetry()
    {
        var (provider, directory, _, _, _) = await CreateDashboardProviderAsync();
        try
        {
            var dashboard = await CreateDashboardAsync(provider);
            var queue = provider.GetRequiredService<TranscriptionQueueService>();
            var progressCard = dashboard.Cards.First(card => card.IsInProgress);

            RaiseStatusChanged(queue, progressCard.Id, TranscriptionStatusChanged.Error, "ffmpeg indisponível");

            Assert.True(progressCard.IsError);
            Assert.Equal("ffmpeg indisponível", progressCard.ErrorMessage);
            Assert.True(progressCard.CanRetry);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task RetryCommand_ResetsStatusToInProgress()
    {
        var (provider, directory, _, errorId, doneId) = await CreateDashboardProviderAsync();
        try
        {
            var dashboard = await CreateDashboardAsync(provider);
            var errorCard = Assert.Single(dashboard.Cards, card => card.Id == errorId);

            errorCard.RetryCommand.Execute(null);
            await Task.Delay(100);

            Assert.True(errorCard.IsInProgress);
            Assert.Null(errorCard.ErrorMessage);

            await using var readContext = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var transcription = await readContext.Transcriptions.SingleAsync(t => t.Id == errorId);
            Assert.Equal(TranscriptionStatus.InProgress, transcription.Status);
            Assert.Null(transcription.ErrorMessage);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    private static void RaiseStatusChanged(
        TranscriptionQueueService queue,
        Guid transcriptionId,
        TranscriptionStatusChanged status,
        string? errorMessage = null)
    {
        var raiseMethod = typeof(TranscriptionQueueService).GetMethod(
            "RaiseStatusChanged",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        raiseMethod!.Invoke(queue, [transcriptionId, status, errorMessage]);
    }

    [Fact]
    public async Task DeleteTranscription_Confirmed_RemovesFromDatabaseAndMedia()
    {
        var confirmation = new FakeConfirmationService { NextResult = true };
        var (provider, directory, _, _, doneId) = await CreateDashboardProviderAsync(confirmation);
        var mediaStorage = provider.GetRequiredService<MediaStorageService>();
        var mediaPath = Path.Combine(directory, "source.wav");
        await File.WriteAllTextAsync(mediaPath, "audio");
        await mediaStorage.CopyToAppDataAsync(mediaPath, doneId);

        try
        {
            var dashboard = await CreateDashboardAsync(provider);
            await dashboard.DeleteTranscriptionAsync(doneId);

            Assert.Equal("Excluir transcrição", confirmation.LastTitle);
            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            Assert.False(await ctx.Transcriptions.AnyAsync(t => t.Id == doneId));
            Assert.False(Directory.Exists(Path.Combine(directory, "media", doneId.ToString("N"))));
            Assert.DoesNotContain(dashboard.Cards, card => card.Id == doneId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task DeleteTranscription_Cancelled_KeepsTranscription()
    {
        var confirmation = new FakeConfirmationService { NextResult = false };
        var (provider, directory, _, _, doneId) = await CreateDashboardProviderAsync(confirmation);

        try
        {
            var dashboard = await CreateDashboardAsync(provider);
            var initialCount = dashboard.Cards.Count;

            await dashboard.DeleteTranscriptionAsync(doneId);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            Assert.True(await ctx.Transcriptions.AnyAsync(t => t.Id == doneId));
            Assert.Equal(initialCount, dashboard.Cards.Count);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
