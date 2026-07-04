using Microsoft.EntityFrameworkCore;
using Transcriba.Core.Data;
using Transcriba.Core.Data.Entities;

namespace Transcriba.Core.Services;

public class LibraryService(IDbContextFactory<TranscribaDbContext> dbContextFactory)
{
    public async Task<IReadOnlyList<TranscriptionSummary>> GetTranscriptions(LibraryFilter filter)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var query = ApplyFilter(context.Transcriptions.AsQueryable(), filter);
        return await ProjectSummariesAsync(query);
    }

    public async Task<IReadOnlyList<TranscriptionSummary>> SearchText(string query, LibraryFilter filter)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await GetTranscriptions(filter);
        }

        await using var context = await dbContextFactory.CreateDbContextAsync();
        var normalized = query.Trim().ToLowerInvariant();
        var baseQuery = ApplyFilter(context.Transcriptions.AsQueryable(), filter);
        var filtered = baseQuery.Where(t =>
            t.Title.ToLower().Contains(normalized) ||
            t.Segments.Any(s => s.Text.ToLower().Contains(normalized)));

        return await ProjectSummariesAsync(filtered);
    }

    public async Task<int> GetCountAsync(LibraryFilter filter)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        return await ApplyFilter(context.Transcriptions.AsQueryable(), filter).CountAsync();
    }

    public async Task<IReadOnlyList<TagSummary>> GetTagsAsync()
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        return await context.Tags
            .Select(t => new TagSummary(
                t.Id,
                t.Name,
                context.Transcriptions.Count(tr => tr.Tags.Any(tag => tag.Id == t.Id))))
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    private static IQueryable<Transcription> ApplyFilter(IQueryable<Transcription> query, LibraryFilter filter)
    {
        query = filter.Status switch
        {
            LibraryStatusFilter.Progress => query.Where(t => t.Status == TranscriptionStatus.InProgress),
            LibraryStatusFilter.Done => query.Where(t => t.Status == TranscriptionStatus.Done),
            _ => query
        };

        if (filter.TagId is int tagId)
        {
            query = query.Where(t => t.Tags.Any(tag => tag.Id == tagId));
        }

        return query;
    }

    private static async Task<IReadOnlyList<TranscriptionSummary>> ProjectSummariesAsync(
        IQueryable<Transcription> query)
    {
        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TranscriptionSummary(
                t.Id,
                t.Title,
                t.Icon,
                t.Status,
                t.CreatedAt,
                t.DurationSeconds,
                t.Speakers.Count,
                t.Tags.Select(tag => tag.Name).ToList(),
                t.Segments
                    .OrderBy(s => s.SortOrder)
                    .Select(s => s.Text)
                    .FirstOrDefault() ?? ""))
            .ToListAsync();
    }
}
