using Microsoft.EntityFrameworkCore;
using Transcriba.Core.Data;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Services;

namespace Transcriba.Tests.Services;

public class LibraryServiceTests
{
    [Fact]
    public async Task GetTranscriptions_FiltersByProgressStatus()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            await SeedSampleTranscriptionsAsync(factory);

            var service = new LibraryService(factory);
            var results = await service.GetTranscriptions(new LibraryFilter(LibraryStatusFilter.Progress));

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal(TranscriptionStatus.InProgress, r.Status));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task GetTranscriptions_FiltersByDoneStatus()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            await SeedSampleTranscriptionsAsync(factory);

            var service = new LibraryService(factory);
            var results = await service.GetTranscriptions(new LibraryFilter(LibraryStatusFilter.Done));

            Assert.Single(results);
            Assert.Equal(TranscriptionStatus.Done, results[0].Status);
            Assert.Equal("Entrevista concluída", results[0].Title);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task GetTranscriptions_FiltersByTag()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var tagId = await SeedSampleTranscriptionsAsync(factory);

            var service = new LibraryService(factory);
            var results = await service.GetTranscriptions(new LibraryFilter(TagId: tagId));

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Contains("mobilidade", r.Tags));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task GetTranscriptions_FiltersUnassignedOnly_ReturnsOnlyTranscriptionsWithoutResearch()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            await using (var ctx = await factory.CreateDbContextAsync())
            {
                var research = new ResearchPage { Title = "Mobilidade", Icon = "🚲", ColorName = "green" };
                ctx.ResearchPages.Add(research);
                await ctx.SaveChangesAsync();

                ctx.Transcriptions.AddRange(
                    new Transcription
                    {
                        Id = Guid.NewGuid(),
                        Title = "Avulsa",
                        Status = TranscriptionStatus.Done,
                        CreatedAt = DateTime.UtcNow,
                        ResearchPageId = null,
                    },
                    new Transcription
                    {
                        Id = Guid.NewGuid(),
                        Title = "Da pesquisa",
                        Status = TranscriptionStatus.Done,
                        CreatedAt = DateTime.UtcNow,
                        ResearchPageId = research.Id,
                    });
                await ctx.SaveChangesAsync();
            }

            var service = new LibraryService(factory);
            var results = await service.GetTranscriptions(new LibraryFilter(UnassignedOnly: true));

            Assert.Single(results);
            Assert.Equal("Avulsa", results[0].Title);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SearchText_FiltersByTitleCaseInsensitive()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            await SeedSampleTranscriptionsAsync(factory);

            var service = new LibraryService(factory);
            var results = await service.SearchText("CONCLUÍDA", new LibraryFilter());

            Assert.Single(results);
            Assert.Equal("Entrevista concluída", results[0].Title);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SearchText_FiltersBySegmentContent()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            await SeedSampleTranscriptionsAsync(factory);

            var service = new LibraryService(factory);
            var results = await service.SearchText("bicicleta", new LibraryFilter());

            Assert.Single(results);
            Assert.Equal("Pesquisa em andamento", results[0].Title);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SearchText_CombinesStatusFilterAndQuery()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            await SeedSampleTranscriptionsAsync(factory);

            var service = new LibraryService(factory);
            var results = await service.SearchText(
                "entrevista",
                new LibraryFilter(LibraryStatusFilter.Progress));

            Assert.Single(results);
            Assert.Equal("Entrevista em progresso", results[0].Title);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SearchText_ReturnsEmptyWhenNoMatch()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            await SeedSampleTranscriptionsAsync(factory);

            var service = new LibraryService(factory);
            var results = await service.SearchText("xyzinexistente", new LibraryFilter());

            Assert.Empty(results);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task GetTranscriptions_ReturnsSummaryFields()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var createdAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            var transcriptionId = Guid.NewGuid();

            await using (var ctx = await factory.CreateDbContextAsync())
            {
                ctx.Transcriptions.Add(new Transcription
                {
                    Id = transcriptionId,
                    Title = "Meta teste",
                    Icon = "🎙️",
                    Status = TranscriptionStatus.Done,
                    CreatedAt = createdAt,
                    DurationSeconds = 120.5,
                    Segments =
                    [
                        new Segment
                        {
                            Id = Guid.NewGuid(),
                            TranscriptionId = transcriptionId,
                            StartSeconds = 0,
                            EndSeconds = 5,
                            Text = "Primeiro trecho de preview",
                            SortOrder = 0
                        }
                    ],
                    Speakers =
                    [
                        new Speaker { Id = Guid.NewGuid(), TranscriptionId = transcriptionId, Name = "A" },
                        new Speaker { Id = Guid.NewGuid(), TranscriptionId = transcriptionId, Name = "B" }
                    ]
                });
                await ctx.SaveChangesAsync();
            }

            var service = new LibraryService(factory);
            var results = await service.GetTranscriptions(new LibraryFilter());

            var summary = Assert.Single(results);
            Assert.Equal(transcriptionId, summary.Id);
            Assert.Equal("Meta teste", summary.Title);
            Assert.Equal("🎙️", summary.Icon);
            Assert.Equal(TranscriptionStatus.Done, summary.Status);
            Assert.Equal(createdAt, summary.Date);
            Assert.Equal(120.5, summary.DurationSeconds);
            Assert.Equal(2, summary.SpeakersCount);
            Assert.Equal("Primeiro trecho de preview", summary.Preview);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task UpdateTranscriptionTagsAsync_ReplacesExistingTagsWithNewSet()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var transcriptionId = Guid.NewGuid();

            await using (var ctx = await factory.CreateDbContextAsync())
            {
                ctx.Transcriptions.Add(new Transcription
                {
                    Id = transcriptionId,
                    Title = "Tags mutáveis",
                    Status = TranscriptionStatus.Done,
                    CreatedAt = DateTime.UtcNow,
                    Tags =
                    [
                        new Tag { Name = "antiga", ColorName = "blue" },
                        new Tag { Name = "descartada", ColorName = "green" }
                    ]
                });
                await ctx.SaveChangesAsync();
            }

            var service = new LibraryService(factory);
            await service.UpdateTranscriptionTagsAsync(transcriptionId, ["nova", "reutilizada", "  descartada  "]);

            var detail = await service.GetTranscriptionDetailAsync(transcriptionId);
            Assert.NotNull(detail);
            var tagNames = detail!.Tags.Select(t => t.Name).Order().ToList();
            Assert.Equal(["descartada", "nova", "reutilizada"], tagNames);

            // A tag "antiga" foi removida desta transcrição, mas a linha global de Tag
            // permanece (Tags são globais/únicas por nome — apenas a associação é trocada).
            await using var ctx2 = await factory.CreateDbContextAsync();
            var stillHasAntigaRow = await ctx2.Tags.AnyAsync(t => t.Name == "antiga");
            Assert.True(stillHasAntigaRow);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task UpdateTranscriptionTagsAsync_WithEmptySet_ClearsAllTags()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var transcriptionId = Guid.NewGuid();

            await using (var ctx = await factory.CreateDbContextAsync())
            {
                ctx.Transcriptions.Add(new Transcription
                {
                    Id = transcriptionId,
                    Title = "Sem tags",
                    Status = TranscriptionStatus.Done,
                    CreatedAt = DateTime.UtcNow,
                    Tags = [new Tag { Name = "remover", ColorName = "blue" }]
                });
                await ctx.SaveChangesAsync();
            }

            var service = new LibraryService(factory);
            await service.UpdateTranscriptionTagsAsync(transcriptionId, []);

            var detail = await service.GetTranscriptionDetailAsync(transcriptionId);
            Assert.NotNull(detail);
            Assert.Empty(detail!.Tags);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    private static async Task<int> SeedSampleTranscriptionsAsync(IDbContextFactory<TranscribaDbContext> factory)
    {
        await using var ctx = await factory.CreateDbContextAsync();

        var tag = new Tag { Name = "mobilidade", ColorName = "blue" };
        ctx.Tags.Add(tag);

        var doneId = Guid.NewGuid();
        var progress1Id = Guid.NewGuid();
        var progress2Id = Guid.NewGuid();

        ctx.Transcriptions.AddRange(
            new Transcription
            {
                Id = doneId,
                Title = "Entrevista concluída",
                Status = TranscriptionStatus.Done,
                CreatedAt = DateTime.UtcNow.AddDays(-2),
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
                Id = progress1Id,
                Title = "Pesquisa em andamento",
                Status = TranscriptionStatus.InProgress,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                Segments =
                [
                    new Segment
                    {
                        Id = Guid.NewGuid(),
                        TranscriptionId = progress1Id,
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
                Id = progress2Id,
                Title = "Entrevista em progresso",
                Status = TranscriptionStatus.InProgress,
                CreatedAt = DateTime.UtcNow,
                Segments =
                [
                    new Segment
                    {
                        Id = Guid.NewGuid(),
                        TranscriptionId = progress2Id,
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
}
