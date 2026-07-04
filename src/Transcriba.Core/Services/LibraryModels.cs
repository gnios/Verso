using Transcriba.Core.Data.Entities;

namespace Transcriba.Core.Services;

public enum LibraryStatusFilter
{
    All,
    Progress,
    Done
}

public sealed record LibraryFilter(LibraryStatusFilter Status = LibraryStatusFilter.All, int? TagId = null);

public sealed record TranscriptionSummary(
    Guid Id,
    string Title,
    string? Icon,
    TranscriptionStatus Status,
    string? ErrorMessage,
    DateTime Date,
    double DurationSeconds,
    int SpeakersCount,
    IReadOnlyList<string> Tags,
    string Preview);

public sealed record TagSummary(int Id, string Name, int Count);
