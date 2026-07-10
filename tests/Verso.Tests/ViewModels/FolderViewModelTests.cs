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

public class FolderViewModelTests
{
    private static async Task<(IServiceProvider Provider, string Directory, int FolderId, Guid DoneId, Guid ProgressId)>
        CreateFolderProviderAsync(FakeConfirmationService? confirmation = null)
    {
        var (baseProvider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "verso.db");

        var services = new ServiceCollection();
        services.AddVersoDatabase(dbPath);
        services.AddVersoEngine();
        services.AddVersoServices();
        services.AddVersoAppServices();
        services.AddSingleton(new MediaStorageService(Path.Combine(directory, "media")));
        if (confirmation is not null)
        {
            services.AddSingleton<IConfirmationService>(confirmation);
        }

        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);

        var folderService = provider.GetRequiredService<FolderService>();
        var folder = await folderService.CreateAsync(
            "Mobilidade urbana",
            "🚲",
            "green");
        folder.Description = "Pesquisa sobre transporte sustentável";
        await using (var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync())
        {
            var page = await ctx.Folders.FindAsync(folder.Id);
            page!.Description = folder.Description;
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
                    FolderId = folder.Id,
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
                    FolderId = folder.Id,
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

        return (provider, directory, folder.Id, doneId, progressId);
    }

    private static async Task<FolderViewModel> CreateFolderPageAsync(
        IServiceProvider provider,
        int folderId)
    {
        var navigation = provider.GetRequiredService<NavigationService>();
        navigation.NavigateTo(
            ScreenKey.Folder,
            new NavigationParameter(FolderId: folderId));

        var page = Assert.IsType<FolderViewModel>(navigation.CurrentViewModel);
        await Task.Delay(50);
        return page;
    }

    [Fact]
    public async Task NavigationParameter_FolderId_LoadsHeaderAndTranscriptions()
    {
        var (provider, directory, folderId, _, _) = await CreateFolderProviderAsync();
        try
        {
            var page = await CreateFolderPageAsync(provider, folderId);

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
    public async Task StatusFilter_Done_ShowsOnlyCompletedTranscriptions()
    {
        var (provider, directory, folderId, doneId, progressId) = await CreateFolderProviderAsync();
        try
        {
            var page = await CreateFolderPageAsync(provider, folderId);
            Assert.Equal(2, page.Transcriptions.Count);

            page.SetDoneFilterCommand.Execute(null);

            Assert.True(page.IsDoneFilterActive);
            Assert.Single(page.Transcriptions);
            Assert.Equal(doneId, page.Transcriptions[0].Id);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task StatusFilter_Progress_ShowsOnlyInProgressTranscriptions()
    {
        var (provider, directory, folderId, _, _) = await CreateFolderProviderAsync();
        try
        {
            var page = await CreateFolderPageAsync(provider, folderId);

            page.SetProgressFilterCommand.Execute(null);

            Assert.True(page.IsProgressFilterActive);
            Assert.Single(page.Transcriptions);
            Assert.Equal(TranscriptionStatus.InProgress, page.Transcriptions[0].Status);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SearchText_FiltersByTitleCaseInsensitive()
    {
        var (provider, directory, folderId, _, _) = await CreateFolderProviderAsync();
        try
        {
            var page = await CreateFolderPageAsync(provider, folderId);

            page.SearchText = "concluída";

            Assert.Single(page.Transcriptions);
            Assert.Contains(page.Transcriptions, c => c.Title.Contains("concluída", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SetAllFilter_AfterFilter_ShowsAllTranscriptions()
    {
        var (provider, directory, folderId, _, _) = await CreateFolderProviderAsync();
        try
        {
            var page = await CreateFolderPageAsync(provider, folderId);
            page.SetDoneFilterCommand.Execute(null);
            Assert.Single(page.Transcriptions);

            page.SetAllFilterCommand.Execute(null);

            Assert.True(page.IsAllFilterActive);
            Assert.Equal(2, page.Transcriptions.Count);
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
            var dbPath = Path.Combine(directory, "verso.db");
            var services = new ServiceCollection();
            services.AddVersoDatabase(dbPath);
            services.AddVersoEngine();
            services.AddVersoServices();
            services.AddVersoAppServices();
            var scopedProvider = services.BuildServiceProvider();
            await DbBootstrapper.MigrateAsync(scopedProvider);

            var folderService = scopedProvider.GetRequiredService<FolderService>();
            var folder = await folderService.CreateAsync("Pasta vazia", "📚", "blue");

            var page = await CreateFolderPageAsync(scopedProvider, folder.Id);

            Assert.Empty(page.Transcriptions);
            Assert.True(page.IsEmpty);
            Assert.Equal("Pasta vazia", page.Title);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task AddTranscription_NavigatesToUploadWithFolderId()
    {
        var (provider, directory, folderId, _, _) = await CreateFolderProviderAsync();
        try
        {
            var page = await CreateFolderPageAsync(provider, folderId);
            var navigation = provider.GetRequiredService<NavigationService>();

            page.AddTranscriptionCommand.Execute(null);

            Assert.Equal(ScreenKey.Upload, navigation.CurrentScreen);
            var parameter = Assert.IsType<NavigationParameter>(navigation.NavigationParameter);
            Assert.Equal(folderId, parameter.FolderId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task OpenTranscription_NavigatesToEditorWithTranscriptionId()
    {
        var (provider, directory, folderId, doneId, _) = await CreateFolderProviderAsync();
        try
        {
            var page = await CreateFolderPageAsync(provider, folderId);
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
        var (provider, directory, folderId, _, _) = await CreateFolderProviderAsync();
        try
        {
            var page = await CreateFolderPageAsync(provider, folderId);
            var navigation = provider.GetRequiredService<NavigationService>();

            page.NavigateDashboardCommand.Execute(null);

            Assert.Equal(ScreenKey.Dashboard, navigation.CurrentScreen);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task DeleteTranscription_Confirmed_RemovesFromListAndDatabase()
    {
        var confirmation = new FakeConfirmationService { NextResult = true };
        var (provider, directory, folderId, doneId, _) =
            await CreateFolderProviderAsync(confirmation);
        var mediaStorage = provider.GetRequiredService<MediaStorageService>();
        var mediaPath = Path.Combine(directory, "source.wav");
        await File.WriteAllTextAsync(mediaPath, "audio");
        await mediaStorage.CopyToStorageAsync(mediaPath, doneId);

        try
        {
            var page = await CreateFolderPageAsync(provider, folderId);
            await page.DeleteTranscriptionAsync(doneId);

            Assert.Equal("Excluir transcrição", confirmation.LastTitle);
            Assert.Single(page.Transcriptions);
            Assert.DoesNotContain(page.Transcriptions, card => card.Id == doneId);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            Assert.False(await ctx.Transcriptions.AnyAsync(t => t.Id == doneId));
            Assert.False(Directory.Exists(Path.Combine(directory, "media", doneId.ToString("N"))));
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
        var (provider, directory, folderId, doneId, _) =
            await CreateFolderProviderAsync(confirmation);

        try
        {
            var page = await CreateFolderPageAsync(provider, folderId);
            await page.DeleteTranscriptionAsync(doneId);

            Assert.Equal(2, page.Transcriptions.Count);
            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            Assert.True(await ctx.Transcriptions.AnyAsync(t => t.Id == doneId));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task DeleteFolder_Confirmed_DissociatesTranscriptionsAndNavigatesToDashboard()
    {
        var confirmation = new FakeConfirmationService { NextResult = true };
        var (provider, directory, folderId, doneId, _) =
            await CreateFolderProviderAsync(confirmation);

        try
        {
            var page = await CreateFolderPageAsync(provider, folderId);
            var navigation = provider.GetRequiredService<NavigationService>();

            await page.DeleteFolderCommand.ExecuteAsync(null);

            Assert.Contains("2 transcrições", confirmation.LastMessage ?? "");
            Assert.Equal(ScreenKey.Dashboard, navigation.CurrentScreen);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            Assert.Null(await ctx.Folders.FindAsync(folderId));
            var transcription = await ctx.Transcriptions.SingleAsync(t => t.Id == doneId);
            Assert.Null(transcription.FolderId);
            Assert.Equal("Entrevista concluída", transcription.Title);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task DeleteFolder_Cancelled_KeepsFolderAndStaysOnPage()
    {
        var confirmation = new FakeConfirmationService { NextResult = false };
        var (provider, directory, folderId, _, _) =
            await CreateFolderProviderAsync(confirmation);

        try
        {
            var page = await CreateFolderPageAsync(provider, folderId);
            var navigation = provider.GetRequiredService<NavigationService>();

            await page.DeleteFolderCommand.ExecuteAsync(null);

            Assert.Equal(ScreenKey.Folder, navigation.CurrentScreen);
            Assert.Equal("Mobilidade urbana", page.Title);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            Assert.NotNull(await ctx.Folders.FindAsync(folderId));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}
