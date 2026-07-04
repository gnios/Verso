using Microsoft.EntityFrameworkCore;
using Transcriba.Core.Catalogs;
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

    public async Task<IReadOnlyList<TranscriptionSummary>> GetTranscriptionsForResearchAsync(int researchPageId)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var query = context.Transcriptions.Where(t => t.ResearchPageId == researchPageId);
        return await ProjectSummariesAsync(query);
    }

    public async Task<IReadOnlyList<TagSummary>> GetTagsAsync()
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        return await context.Tags
            .OrderBy(t => t.Name)
            .Select(t => new TagSummary(
                t.Id,
                t.Name,
                context.Transcriptions.Count(tr => tr.Tags.Any(tag => tag.Id == t.Id))))
            .ToListAsync();
    }

    public async Task<Transcription> CreateForUploadAsync(
        Guid id,
        string title,
        string mediaFilePath,
        string language,
        ModelQuality quality,
        SpeakerMode speakerMode,
        int? researchPageId)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();

        var transcription = new Transcription
        {
            Id = id,
            Title = title,
            Icon = "📝",
            Status = TranscriptionStatus.InProgress,
            MediaFilePath = mediaFilePath,
            Language = language,
            Quality = quality,
            SpeakerMode = speakerMode,
            ResearchPageId = researchPageId,
            CreatedAt = DateTime.UtcNow,
        };

        context.Transcriptions.Add(transcription);
        await context.SaveChangesAsync();
        return transcription;
    }

    public async Task<Transcription?> GetTranscriptionAsync(Guid id)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        return await context.Transcriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id);
    }

    public async Task<Transcription?> GetTranscriptionDetailAsync(Guid id)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        return await context.Transcriptions
            .Include(t => t.Segments.OrderBy(s => s.SortOrder))
            .ThenInclude(s => s.Speaker)
            .Include(t => t.Speakers)
            .Include(t => t.Tags)
            .Include(t => t.ResearchPage)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task UpdateTranscriptionTitleAsync(Guid id, string title)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var updated = await context.Transcriptions
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.Title, title));

        if (updated == 0)
        {
            throw new InvalidOperationException($"Transcrição {id} não encontrada.");
        }
    }

    public async Task UpdateTranscriptionIconAsync(Guid id, string? icon)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var updated = await context.Transcriptions
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.Icon, icon));

        if (updated == 0)
        {
            throw new InvalidOperationException($"Transcrição {id} não encontrada.");
        }
    }

    public async Task UpdateSegmentTextAsync(Guid segmentId, string text)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var updated = await context.Segments
            .Where(s => s.Id == segmentId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Text, text));

        if (updated == 0)
        {
            throw new InvalidOperationException($"Segmento {segmentId} não encontrado.");
        }
    }

    public async Task ApplySegmentSplitAsync(
        Guid transcriptionId,
        Guid segmentId,
        string beforeText,
        Segment afterSegment)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var segment = await context.Segments
            .SingleOrDefaultAsync(s => s.Id == segmentId && s.TranscriptionId == transcriptionId);

        if (segment is null)
        {
            throw new InvalidOperationException($"Segmento {segmentId} não encontrado.");
        }

        segment.Text = beforeText;
        var insertOrder = segment.SortOrder + 1;

        var toShift = await context.Segments
            .Where(s => s.TranscriptionId == transcriptionId && s.SortOrder >= insertOrder)
            .ToListAsync();

        foreach (var shifted in toShift)
        {
            shifted.SortOrder++;
        }

        context.Segments.Add(new Segment
        {
            Id = afterSegment.Id,
            TranscriptionId = transcriptionId,
            StartSeconds = afterSegment.StartSeconds,
            EndSeconds = afterSegment.EndSeconds,
            Text = afterSegment.Text,
            SortOrder = insertOrder,
            SpeakerId = afterSegment.SpeakerId,
        });

        await context.SaveChangesAsync();
    }

    public async Task ApplySegmentMergeAsync(
        Guid transcriptionId,
        Guid previousSegmentId,
        string mergedText,
        Guid removeSegmentId)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var previous = await context.Segments
            .SingleOrDefaultAsync(s => s.Id == previousSegmentId && s.TranscriptionId == transcriptionId);
        var remove = await context.Segments
            .SingleOrDefaultAsync(s => s.Id == removeSegmentId && s.TranscriptionId == transcriptionId);

        if (previous is null || remove is null)
        {
            throw new InvalidOperationException("Segmentos para mesclagem não encontrados.");
        }

        var removedOrder = remove.SortOrder;
        previous.Text = mergedText;
        context.Segments.Remove(remove);

        var toShift = await context.Segments
            .Where(s => s.TranscriptionId == transcriptionId && s.SortOrder > removedOrder)
            .ToListAsync();

        foreach (var shifted in toShift)
        {
            shifted.SortOrder--;
        }

        await context.SaveChangesAsync();
    }

    public async Task AssignSpeakerToSegmentAsync(Guid segmentId, Guid speakerId)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var updated = await context.Segments
            .Where(s => s.Id == segmentId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.SpeakerId, speakerId));

        if (updated == 0)
        {
            throw new InvalidOperationException($"Segmento {segmentId} não encontrado.");
        }
    }

    public async Task ResetToInProgressAsync(Guid id)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var updated = await context.Transcriptions
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.Status, TranscriptionStatus.InProgress)
                .SetProperty(t => t.ErrorMessage, (string?)null));

        if (updated == 0)
        {
            throw new InvalidOperationException($"Transcrição {id} não encontrada.");
        }
    }

    public async Task<Transcription> CreateStandaloneAsync(
        string title,
        string? icon,
        IEnumerable<string> tagNames)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();

        var transcription = new Transcription
        {
            Id = Guid.NewGuid(),
            Title = title,
            Icon = icon,
            Status = TranscriptionStatus.InProgress,
            CreatedAt = DateTime.UtcNow,
        };

        foreach (var tagName in tagNames
                     .Select(t => t.Trim())
                     .Where(t => !string.IsNullOrEmpty(t))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var tag = await context.Tags.FirstOrDefaultAsync(t => t.Name == tagName);
            if (tag is null)
            {
                tag = new Tag
                {
                    Name = tagName,
                    ColorName = TagColorCatalog.GetColor(tagName),
                };
                context.Tags.Add(tag);
            }

            transcription.Tags.Add(tag);
        }

        context.Transcriptions.Add(transcription);
        await context.SaveChangesAsync();
        return transcription;
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
                t.ErrorMessage,
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
