namespace Transcriba.Core.Engine;

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

public enum TranscriptionStatusChanged
{
    Queued,
    InProgress,
    Done,
    Error,
}
