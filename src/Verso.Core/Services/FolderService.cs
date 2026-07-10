using Microsoft.EntityFrameworkCore;
using Verso.Core.Data;
using Verso.Core.Data.Entities;

namespace Verso.Core.Services;

public class FolderService(IDbContextFactory<VersoDbContext> dbContextFactory)
{
    public async Task<Folder> CreateAsync(string title, string icon, string colorName)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();

        var page = new Folder
        {
            Title = title,
            Icon = icon,
            ColorName = colorName,
            CreatedAt = DateTime.UtcNow
        };

        context.Folders.Add(page);
        await context.SaveChangesAsync();
        return page;
    }

    public async Task<Folder?> GetByIdAsync(int id)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        return await context.Folders
            .Include(r => r.Transcriptions)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<IReadOnlyList<Folder>> GetAllAsync()
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        return await context.Folders
            .Include(r => r.Transcriptions)
            .OrderByDescending(r => r.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();

        var page = await context.Folders
            .Include(r => r.Transcriptions)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (page is null)
        {
            return;
        }

        foreach (var transcription in page.Transcriptions)
        {
            transcription.FolderId = null;
        }

        context.Folders.Remove(page);
        await context.SaveChangesAsync();
    }

    public async Task AssignTranscriptionToFolderAsync(Guid transcriptionId, int? folderId)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var transcription = await context.Transcriptions.FindAsync(transcriptionId);
        if (transcription is null)
        {
            throw new InvalidOperationException($"Transcrição {transcriptionId} não encontrada.");
        }

        if (folderId is int id)
        {
            var exists = await context.Folders.AnyAsync(r => r.Id == id);
            if (!exists)
            {
                throw new InvalidOperationException($"Pasta {id} não encontrada.");
            }
        }

        transcription.FolderId = folderId;
        await context.SaveChangesAsync();
    }
}
