using Transcriba.Core.Data.Entities;

namespace Transcriba.Core.Engine;

public sealed record TranscriptionJobRequest(
    Guid TranscriptionId,
    string MediaFilePath,
    string Language,
    ModelQuality Quality,
    ExecutionDevice Device);

public sealed record TranscriptionSegmentResult(
    double StartSeconds,
    double EndSeconds,
    string Text,
    string? SpeakerName = null);

public sealed record TranscriptionResult(IReadOnlyList<TranscriptionSegmentResult> Segments);

public sealed record EngineProgress(string Stage, int? PartIndex = null, int? TotalParts = null);
