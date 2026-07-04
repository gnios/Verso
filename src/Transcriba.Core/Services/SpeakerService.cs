using Microsoft.EntityFrameworkCore;
using Transcriba.Core.Catalogs;
using Transcriba.Core.Data;
using Transcriba.Core.Data.Entities;

namespace Transcriba.Core.Services;

public class SpeakerService(IDbContextFactory<TranscribaDbContext> dbContextFactory)
{
    public async Task<IReadOnlyList<Speaker>> GetSpeakersAsync(Guid transcriptionId)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        return await context.Speakers
            .Where(s => s.TranscriptionId == transcriptionId)
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<Speaker> CreateSpeakerAsync(Guid transcriptionId, string name)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();

        var existingCount = await context.Speakers
            .CountAsync(s => s.TranscriptionId == transcriptionId);

        var speaker = new Speaker
        {
            Id = Guid.NewGuid(),
            TranscriptionId = transcriptionId,
            Name = name,
            ColorHex = SpeakerColorCatalog.ColorAtIndex(existingCount)
        };

        context.Speakers.Add(speaker);
        await context.SaveChangesAsync();
        return speaker;
    }
}
