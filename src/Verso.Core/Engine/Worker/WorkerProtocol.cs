using System.Text.Json;
using System.Text.Json.Serialization;

namespace Verso.Core.Engine.Worker;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(WorkerJobMessage), "job")]
[JsonDerivedType(typeof(WorkerCancelMessage), "cancel")]
[JsonDerivedType(typeof(WorkerProgressMessage), "progress")]
[JsonDerivedType(typeof(WorkerResultMessage), "result")]
[JsonDerivedType(typeof(WorkerErrorMessage), "error")]
public abstract record WorkerMessage;

public sealed record WorkerJobMessage(TranscriptionJobRequest Request) : WorkerMessage;
public sealed record WorkerCancelMessage : WorkerMessage;
public sealed record WorkerProgressMessage(string Stage, int? PartIndex, int? TotalParts) : WorkerMessage;
public sealed record WorkerResultMessage(TranscriptionResult Result) : WorkerMessage;
public sealed record WorkerErrorMessage(string Message) : WorkerMessage;

/// <summary>
/// Opções JSON compartilhadas entre pai (<c>WorkerProcessTranscriptionEngine</c>) e worker
/// (<c>WorkerHost</c>) para serializar/desserializar as mensagens do protocolo NDJSON.
/// Reflection-based (sem source-gen), mesmo padrão de <see cref="Verso.Core.Services.FeedbackService"/>.
/// </summary>
public static class WorkerProtocol
{
    public static readonly JsonSerializerOptions JsonOptions = new();
}
