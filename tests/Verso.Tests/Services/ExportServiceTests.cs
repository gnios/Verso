using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Verso.Core.Data;
using Verso.Core.Data.Entities;
using Verso.Core.Export;

namespace Verso.Tests.Services;

public class ExportServiceTests
{
    [Fact]
    public async Task ExportTxtAsync_SpeakerAndTimestampOnSeparateLineFromText()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var outputPath = Path.Combine(directory, "export.txt");

        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var transcriptionId = await SeedTranscriptionWithSegmentsAsync(factory);
            var service = new ExportService(factory);

            await service.ExportTxtAsync(transcriptionId, outputPath);

            var lines = await File.ReadAllLinesAsync(outputPath, Encoding.UTF8);
            Assert.Equal("Transcrição", lines[0]);
            Assert.Equal("[Ana] — [00:00]", lines[1]);
            Assert.Equal("Olá mundo", lines[2]);
            Assert.Equal("[Ana] — [00:02]", lines[3]);
            Assert.Equal("Tchau", lines[4]);
            Assert.Equal(5, lines.Length);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ExportTxtAsync_PreservesSegmentOrderByTimestamp()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var outputPath = Path.Combine(directory, "export-order.txt");

        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var transcriptionId = await SeedMultiSpeakerTranscriptionAsync(factory);
            var service = new ExportService(factory);

            await service.ExportTxtAsync(transcriptionId, outputPath);

            var lines = await File.ReadAllLinesAsync(outputPath, Encoding.UTF8);
            Assert.Equal("Transcrição", lines[0]);
            Assert.Equal("[Ana] — [00:00]", lines[1]);
            Assert.Equal("Bom dia", lines[2]);
            // Linha em branco entre locutores diferentes
            Assert.Equal("", lines[3]);
            Assert.Equal("[Carlos] — [00:03]", lines[4]);
            Assert.Equal("Olá", lines[5]);
            Assert.Equal("", lines[6]);
            Assert.Equal("[Ana] — [00:06]", lines[7]);
            Assert.Equal("Como vai", lines[8]);
            Assert.Equal(9, lines.Length);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ExportSrtAsync_WritesNumberedCuesWithSpeakerPrefix()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var outputPath = Path.Combine(directory, "export.srt");

        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var transcriptionId = await SeedTranscriptionWithSegmentsAsync(factory);
            var service = new ExportService(factory);

            await service.ExportSrtAsync(transcriptionId, outputPath);

            var content = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
            Assert.StartsWith("1", content.TrimStart(), StringComparison.Ordinal);
            Assert.Contains("-->", content, StringComparison.Ordinal);
            Assert.Contains("Ana: Olá mundo", content, StringComparison.Ordinal);
            Assert.Matches(@"\d{2}:\d{2}:\d{2},\d{3}", content);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ExportVttAsync_WritesWebVttWithSpeakerPrefix()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var outputPath = Path.Combine(directory, "export.vtt");

        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var transcriptionId = await SeedTranscriptionWithSegmentsAsync(factory);
            var service = new ExportService(factory);

            await service.ExportVttAsync(transcriptionId, outputPath);

            var content = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
            Assert.StartsWith("WEBVTT", content, StringComparison.Ordinal);
            Assert.Contains("Ana: Tchau", content, StringComparison.Ordinal);
            Assert.Matches(@"\d{2}:\d{2}:\d{2}\.\d{3}", content);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ExportTxtAsync_ThrowsWhenTranscriptionHasNoSegments()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var outputPath = Path.Combine(directory, "empty.txt");

        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var transcriptionId = Guid.NewGuid();

            await using (var ctx = await factory.CreateDbContextAsync())
            {
                ctx.Transcriptions.Add(new Transcription
                {
                    Id = transcriptionId,
                    Title = "Vazia",
                    Status = TranscriptionStatus.InProgress
                });
                await ctx.SaveChangesAsync();
            }

            var service = new ExportService(factory);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ExportTxtAsync(transcriptionId, outputPath));

            Assert.Contains("Não há conteúdo", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    private static async Task<Guid> SeedTranscriptionWithSegmentsAsync(
        IDbContextFactory<VersoDbContext> factory)
    {
        var transcriptionId = Guid.NewGuid();
        var speakerId = Guid.NewGuid();

        await using var ctx = await factory.CreateDbContextAsync();
        ctx.Transcriptions.Add(new Transcription
        {
            Id = transcriptionId,
            Title = "Entrevista teste",
            Status = TranscriptionStatus.Done,
            Language = "pt",
            Quality = ModelQuality.Standard,
            DurationSeconds = 10,
            MediaFilePath = @"C:\media\entrevista.mp3",
            Speakers =
            [
                new Speaker { Id = speakerId, TranscriptionId = transcriptionId, Name = "Ana", ColorHex = "#2eaadc" }
            ],
            Segments =
            [
                new Segment
                {
                    Id = Guid.NewGuid(),
                    TranscriptionId = transcriptionId,
                    StartSeconds = 0,
                    EndSeconds = 2.5,
                    Text = "Olá mundo",
                    SortOrder = 0,
                    SpeakerId = speakerId
                },
                new Segment
                {
                    Id = Guid.NewGuid(),
                    TranscriptionId = transcriptionId,
                    StartSeconds = 2.5,
                    EndSeconds = 5,
                    Text = "Tchau",
                    SortOrder = 1,
                    SpeakerId = speakerId
                }
            ]
        });
        await ctx.SaveChangesAsync();
        return transcriptionId;
    }

    private static async Task<Guid> SeedMultiSpeakerTranscriptionAsync(
        IDbContextFactory<VersoDbContext> factory)
    {
        var transcriptionId = Guid.NewGuid();
        var anaId = Guid.NewGuid();
        var carlosId = Guid.NewGuid();

        await using var ctx = await factory.CreateDbContextAsync();
        ctx.Transcriptions.Add(new Transcription
        {
            Id = transcriptionId,
            Title = "Diálogo",
            Status = TranscriptionStatus.Done,
            Language = "pt",
            Quality = ModelQuality.Standard,
            DurationSeconds = 30,
            MediaFilePath = null,
            Speakers =
            [
                new Speaker { Id = anaId, TranscriptionId = transcriptionId, Name = "Ana", ColorHex = "#2eaadc" },
                new Speaker { Id = carlosId, TranscriptionId = transcriptionId, Name = "Carlos", ColorHex = "#e74c3c" },
            ],
            Segments =
            [
                new Segment
                {
                    Id = Guid.NewGuid(), TranscriptionId = transcriptionId,
                    StartSeconds = 0, EndSeconds = 3, Text = "Bom dia", SortOrder = 0, SpeakerId = anaId
                },
                new Segment
                {
                    Id = Guid.NewGuid(), TranscriptionId = transcriptionId,
                    StartSeconds = 3, EndSeconds = 6, Text = "Olá", SortOrder = 1, SpeakerId = carlosId
                },
                new Segment
                {
                    Id = Guid.NewGuid(), TranscriptionId = transcriptionId,
                    StartSeconds = 6, EndSeconds = 9, Text = "Como vai", SortOrder = 2, SpeakerId = anaId
                },
            ]
        });
        await ctx.SaveChangesAsync();
        return transcriptionId;
    }
}
