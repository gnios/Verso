using Microsoft.EntityFrameworkCore;
using Verso.Core.Catalogs;
using Verso.Core.Data;
using Verso.Core.Data.Entities;

namespace Verso.Tests.Services;

public class SpeakerServiceTests
{
    [Fact]
    public async Task CreateSpeakerAsync_AssignsColorsInPaletteCycle()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var transcriptionId = await SeedTranscriptionAsync(factory);
            var service = new Verso.Core.Services.SpeakerService(factory);

            var colors = new List<string>();
            for (var i = 0; i < 9; i++)
            {
                var speaker = await service.CreateSpeakerAsync(transcriptionId, $"Locutor {i + 1}");
                colors.Add(speaker.ColorHex);
            }

            Assert.Equal(SpeakerColorCatalog.Colors[0], colors[0]);
            Assert.Equal(SpeakerColorCatalog.Colors[7], colors[7]);
            Assert.Equal(SpeakerColorCatalog.Colors[0], colors[8]);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task GetSpeakersAsync_ReturnsOnlySpeakersFromRequestedTranscription()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var transcriptionA = await SeedTranscriptionAsync(factory);
            var transcriptionB = await SeedTranscriptionAsync(factory);
            var service = new Verso.Core.Services.SpeakerService(factory);

            await service.CreateSpeakerAsync(transcriptionA, "Ana");
            await service.CreateSpeakerAsync(transcriptionB, "Bruno");

            var speakersA = await service.GetSpeakersAsync(transcriptionA);
            var speakersB = await service.GetSpeakersAsync(transcriptionB);

            Assert.Single(speakersA);
            Assert.Equal("Ana", speakersA[0].Name);
            Assert.Single(speakersB);
            Assert.Equal("Bruno", speakersB[0].Name);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task RenameSpeakerAsync_UpdatesPersistedName()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var transcriptionId = await SeedTranscriptionAsync(factory);
            var service = new Verso.Core.Services.SpeakerService(factory);
            var speaker = await service.CreateSpeakerAsync(transcriptionId, "Ana");

            await service.RenameSpeakerAsync(speaker.Id, "Ana Paula");

            var speakers = await service.GetSpeakersAsync(transcriptionId);
            Assert.Single(speakers);
            Assert.Equal("Ana Paula", speakers[0].Name);
            Assert.Equal(speaker.ColorHex, speakers[0].ColorHex);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task RenameSpeakerAsync_WithEmptyName_LeavesSpeakerUnchanged()
    {
        var (provider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        try
        {
            var factory = TestDbHelper.GetFactory(provider);
            var transcriptionId = await SeedTranscriptionAsync(factory);
            var service = new Verso.Core.Services.SpeakerService(factory);
            var speaker = await service.CreateSpeakerAsync(transcriptionId, "Bruno");

            await service.RenameSpeakerAsync(speaker.Id, "   ");

            var speakers = await service.GetSpeakersAsync(transcriptionId);
            Assert.Single(speakers);
            Assert.Equal("Bruno", speakers[0].Name);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    private static async Task<Guid> SeedTranscriptionAsync(IDbContextFactory<VersoDbContext> factory)
    {
        var id = Guid.NewGuid();
        await using var ctx = await factory.CreateDbContextAsync();
        ctx.Transcriptions.Add(new Transcription
        {
            Id = id,
            Title = "Teste",
            Status = TranscriptionStatus.Done
        });
        await ctx.SaveChangesAsync();
        return id;
    }
}
