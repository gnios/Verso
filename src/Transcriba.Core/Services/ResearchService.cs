using Microsoft.EntityFrameworkCore;
using Transcriba.Core.Data;
using Transcriba.Core.Data.Entities;

namespace Transcriba.Core.Services;

public class ResearchService(IDbContextFactory<TranscribaDbContext> dbContextFactory)
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
}
