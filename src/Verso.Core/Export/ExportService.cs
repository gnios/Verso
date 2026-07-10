using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Verso.Core.Data;
using Verso.Core.Data.Entities;

namespace Verso.Core.Export;

public class ExportService(IDbContextFactory<VersoDbContext> dbContextFactory)
{
    public async Task ExportTxtAsync(Guid transcriptionId, string destPath)
    {
        var transcription = await LoadTranscriptionAsync(transcriptionId);
        EnsureHasSegments(transcription);

        var builder = new StringBuilder();
        builder.AppendLine("Transcrição");

        string? previousSpeaker = null;
        foreach (var segment in transcription.Segments.OrderBy(s => s.SortOrder))
        {
            var speaker = segment.Speaker?.Name;

            // Linha em branco entre diálogos de locutores diferentes
            if (previousSpeaker is not null && speaker != previousSpeaker)
            {
                builder.AppendLine();
            }

            var timestamp = TranscriptionTextFormatter.FormatTxtTimestamp(segment.StartSeconds);
            if (string.IsNullOrEmpty(speaker))
            {
                builder.AppendLine($" — [{timestamp}]");
            }
            else
            {
                builder.AppendLine($"[{speaker}] — [{timestamp}]");
            }

            builder.AppendLine(segment.Text.Trim());
            previousSpeaker = speaker;
        }

        await File.WriteAllTextAsync(destPath, builder.ToString(), Encoding.UTF8);
    }

    public async Task ExportSrtAsync(Guid transcriptionId, string destPath)
    {
        var transcription = await LoadTranscriptionAsync(transcriptionId);
        EnsureHasSegments(transcription);

        var builder = new StringBuilder();
        var segments = transcription.Segments.OrderBy(s => s.SortOrder).ToList();

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            builder.AppendLine((i + 1).ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(
                $"{TranscriptionTextFormatter.FormatSrtTimestamp(segment.StartSeconds)} --> {TranscriptionTextFormatter.FormatSrtTimestamp(segment.EndSeconds)}");
            builder.AppendLine(TranscriptionTextFormatter.BuildCueText(segment.Speaker?.Name, segment.Text));
            builder.AppendLine();
        }

        await File.WriteAllTextAsync(destPath, builder.ToString(), Encoding.UTF8);
    }

    public async Task ExportVttAsync(Guid transcriptionId, string destPath)
    {
        var transcription = await LoadTranscriptionAsync(transcriptionId);
        EnsureHasSegments(transcription);

        var builder = new StringBuilder();
        builder.AppendLine("WEBVTT");
        builder.AppendLine();

        foreach (var segment in transcription.Segments.OrderBy(s => s.SortOrder))
        {
            builder.AppendLine(
                $"{TranscriptionTextFormatter.FormatVttTimestamp(segment.StartSeconds)} --> {TranscriptionTextFormatter.FormatVttTimestamp(segment.EndSeconds)}");
            builder.AppendLine(TranscriptionTextFormatter.BuildCueText(segment.Speaker?.Name, segment.Text));
            builder.AppendLine();
        }

        await File.WriteAllTextAsync(destPath, builder.ToString(), Encoding.UTF8);
    }

    private async Task<Transcription> LoadTranscriptionAsync(Guid transcriptionId)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var transcription = await context.Transcriptions
            .Include(t => t.Segments)
            .ThenInclude(s => s.Speaker)
            .FirstOrDefaultAsync(t => t.Id == transcriptionId);

        if (transcription is null)
        {
            throw new InvalidOperationException($"Transcrição {transcriptionId} não encontrada.");
        }

        return transcription;
    }

    private static void EnsureHasSegments(Transcription transcription)
    {
        if (transcription.Segments.Count == 0)
        {
            throw new InvalidOperationException("Não há conteúdo para exportar.");
        }
    }
}
