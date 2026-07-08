namespace Verso.Core.Engine;

public interface ITranscriptionEngine
{
    Task<TranscriptionResult> TranscribeAsync(
        TranscriptionJobRequest request,
        IProgress<EngineProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class WhisperTranscriptionEngineAdapter(WhisperTranscriptionEngine engine) : ITranscriptionEngine
{
    public Task<TranscriptionResult> TranscribeAsync(
        TranscriptionJobRequest request,
        IProgress<EngineProgress>? progress,
        CancellationToken cancellationToken) =>
        engine.TranscribeAsync(request, progress, cancellationToken);
}

public sealed class TranscriptionStatusChangedEventArgs(Guid transcriptionId, TranscriptionStatusChanged status) : EventArgs
{
    public Guid TranscriptionId { get; } = transcriptionId;
    public TranscriptionStatusChanged Status { get; } = status;
    public string? ErrorMessage { get; init; }
}

public sealed class TranscriptionProgressEventArgs(Guid transcriptionId, string stage, int? partIndex, int? totalParts) : EventArgs
{
    public Guid TranscriptionId { get; } = transcriptionId;
    public string Stage { get; } = stage;
    public int? PartIndex { get; } = partIndex;
    public int? TotalParts { get; } = totalParts;
    public int? Percent => PartIndex is int i && TotalParts is int t && t > 0
        ? (int)Math.Round(i * 100.0 / t)
        : null;
}

public enum TranscriptionStatusChanged
{
    Queued,
    InProgress,
    Done,
    Error,
}
