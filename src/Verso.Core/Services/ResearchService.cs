using Microsoft.EntityFrameworkCore;
using Verso.Core.Data;
using Verso.Core.Data.Entities;

namespace Verso.Core.Services;

public class ResearchService(IDbContextFactory<VersoDbContext> dbContextFactory)
{
    public async Task<ResearchPage> CreateAsync(string title, string icon, string colorName)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();

        var page = new ResearchPage
        {
            Title = title,
            Icon = icon,
            ColorName = colorName,
            CreatedAt = DateTime.UtcNow
        };

        context.ResearchPages.Add(page);
        await context.SaveChangesAsync();
        return page;
    }

    public async Task<ResearchPage?> GetByIdAsync(int id)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        return await context.ResearchPages
            .Include(r => r.Transcriptions)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<IReadOnlyList<ResearchPage>> GetAllAsync()
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        return await context.ResearchPages
            .Include(r => r.Transcriptions)
            .OrderByDescending(r => r.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();

        var page = await context.ResearchPages
            .Include(r => r.Transcriptions)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (page is null)
        {
            return;
        }

        foreach (var transcription in page.Transcriptions)
        {
            transcription.ResearchPageId = null;
        }

        context.ResearchPages.Remove(page);
        await context.SaveChangesAsync();
    }

    public async Task AssignTranscriptionToResearchAsync(Guid transcriptionId, int? researchPageId)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var transcription = await context.Transcriptions.FindAsync(transcriptionId);
        if (transcription is null)
        {
            throw new InvalidOperationException($"Transcrição {transcriptionId} não encontrada.");
        }

        if (researchPageId is int id)
        {
            var exists = await context.ResearchPages.AnyAsync(r => r.Id == id);
            if (!exists)
            {
                throw new InvalidOperationException($"Pesquisa {id} não encontrada.");
            }
        }

        transcription.ResearchPageId = researchPageId;
        await context.SaveChangesAsync();
    }
}
