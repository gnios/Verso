using Microsoft.EntityFrameworkCore;
using Verso.Core.Data;
using Verso.Core.Data.Entities;
using Verso.Core.Services;

namespace Verso.Tests.Services;

public class ResearchServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsResearchPage()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var service = new ResearchService(factory);

            var created = await service.CreateAsync("Mobilidade urbana", "🚲", "green");

            Assert.True(created.Id > 0);
            Assert.Equal("Mobilidade urbana", created.Title);
            Assert.Equal("🚲", created.Icon);
            Assert.Equal("green", created.ColorName);

            var loaded = await service.GetByIdAsync(created.Id);
            Assert.NotNull(loaded);
            Assert.Equal(created.Id, loaded.Id);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullWhenMissing()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var service = new ResearchService(factory);

            var loaded = await service.GetByIdAsync(9999);

            Assert.Null(loaded);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesPageWithoutTranscriptions()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var service = new ResearchService(factory);
            var created = await service.CreateAsync("Tese vazia", "📚", "blue");

            await service.DeleteAsync(created.Id);

            Assert.Null(await service.GetByIdAsync(created.Id));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task DeleteAsync_DissociatesTranscriptionsWithoutDeletingThem()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var service = new ResearchService(factory);
            var page = await service.CreateAsync("Pesquisa com dados", "📚", "purple");

            var transcriptionId = Guid.NewGuid();
            await using (var ctx = await factory.CreateDbContextAsync())
            {
                ctx.Transcriptions.Add(new Transcription
                {
                    Id = transcriptionId,
                    Title = "Entrevista vinculada",
                    Status = TranscriptionStatus.Done,
                    ResearchPageId = page.Id
                });
                await ctx.SaveChangesAsync();
            }

            await service.DeleteAsync(page.Id);

            Assert.Null(await service.GetByIdAsync(page.Id));

            await using var readCtx = await factory.CreateDbContextAsync();
            var transcription = await readCtx.Transcriptions.SingleAsync(t => t.Id == transcriptionId);
            Assert.Null(transcription.ResearchPageId);
            Assert.Equal("Entrevista vinculada", transcription.Title);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
    [Fact]
    public async Task AssignTranscriptionToResearchAsync_LinksTranscriptionToResearch()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var service = new ResearchService(factory);
            var page = await service.CreateAsync("Pesquisa alvo", "🔬", "green");

            var transcriptionId = Guid.NewGuid();
            await using (var ctx = await factory.CreateDbContextAsync())
            {
                ctx.Transcriptions.Add(new Transcription
                {
                    Id = transcriptionId,
                    Title = "Solta",
                    Status = TranscriptionStatus.Done
                });
                await ctx.SaveChangesAsync();
            }

            await service.AssignTranscriptionToResearchAsync(transcriptionId, page.Id);

            await using var readCtx = await factory.CreateDbContextAsync();
            var transcription = await readCtx.Transcriptions.SingleAsync(t => t.Id == transcriptionId);
            Assert.Equal(page.Id, transcription.ResearchPageId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task AssignTranscriptionToResearchAsync_WithNullId_UnlinksTranscription()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var service = new ResearchService(factory);
            var page = await service.CreateAsync("Pesquisa origem", "📚", "blue");

            var transcriptionId = Guid.NewGuid();
            await using (var ctx = await factory.CreateDbContextAsync())
            {
                ctx.Transcriptions.Add(new Transcription
                {
                    Id = transcriptionId,
                    Title = "Vinculada",
                    Status = TranscriptionStatus.Done,
                    ResearchPageId = page.Id
                });
                await ctx.SaveChangesAsync();
            }

            await service.AssignTranscriptionToResearchAsync(transcriptionId, null);

            await using var readCtx = await factory.CreateDbContextAsync();
            var transcription = await readCtx.Transcriptions.SingleAsync(t => t.Id == transcriptionId);
            Assert.Null(transcription.ResearchPageId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task AssignTranscriptionToResearchAsync_WithUnknownResearch_Throws()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var service = new ResearchService(factory);
            var transcriptionId = Guid.NewGuid();
            await using (var ctx = await factory.CreateDbContextAsync())
            {
                ctx.Transcriptions.Add(new Transcription
                {
                    Id = transcriptionId,
                    Title = "X",
                    Status = TranscriptionStatus.Done
                });
                await ctx.SaveChangesAsync();
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AssignTranscriptionToResearchAsync(transcriptionId, 9999));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

}
