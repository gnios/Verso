using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Photino.NET;

namespace Verso.App.Services;

/// <summary>
/// Ponte nativa Photino (JS ⇄ C#) com protocolo JSON request/response:
/// <code>
///   JS → C#  {"id": n, "method": "...", "args": {...}}  via window.external.sendMessage
///   C# → JS  {"id": n, "ok": true, "result": {...}}      via SendWebMessage
///            {"id": n, "ok": false, "error": "..."}
///   C# → JS  {"event": "nome", "payload": {...}}          (push de eventos)
/// </code>
/// No JS, as respostas chegam por <c>window.external.receiveMessage</c> (ver ui/src/lib/bridge.ts).
/// </summary>
public sealed class NativeBridge
{
    private readonly PhotinoWindow _window;
    private readonly ILogger<NativeBridge> _logger;
    private readonly ConcurrentDictionary<string, Func<JsonElement?, Task<object?>>> _handlers = new();
    // Enums como string: o JS tipa filters/engine/status por nome ('Done', 'Parakeet'),
    // e eventos que ecoam o NavigationParameter voltam a casar com o tipo do cliente.
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private long _lastMessageAtTicks = DateTime.UtcNow.Ticks;
    private bool _hasWebViewConnection;

    public NativeBridge(PhotinoWindow window, ILogger<NativeBridge> logger)
    {
        _window = window;
        _logger = logger;
    }

    /// <summary>Último instante em que o WebView enviou mensagem — o watchdog usa
    /// para detectar renderer morto (página em branco sem mensagens).</summary>
    public DateTime LastMessageAt => new(Interlocked.Read(ref _lastMessageAtTicks), DateTimeKind.Utc);

    public void Register(string method, Func<JsonElement?, Task<object?>> handler) =>
        _handlers[method] = handler;

    public void HandleMessage(string raw)
    {
        Interlocked.Exchange(ref _lastMessageAtTicks, DateTime.UtcNow.Ticks);
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            long? id = root.TryGetProperty("id", out var idElement) &&
                       idElement.ValueKind == JsonValueKind.Number
                ? idElement.GetInt64()
                : null;
            var method = root.TryGetProperty("method", out var methodElement)
                ? methodElement.GetString()
                : null;
            JsonElement? args = root.TryGetProperty("args", out var argsElement) &&
                                argsElement.ValueKind == JsonValueKind.Object
                ? argsElement.Clone() // Clone: o JsonDocument é descartado no fim do using — o handler roda assíncrono.
                : null;

            if (string.IsNullOrWhiteSpace(method))
            {
                return;
            }

            Volatile.Write(ref _hasWebViewConnection, true);

            // Keep-alive do watchdog: responde na hora, sem lookup de handler.
            if (method == "app.ping")
            {
                Respond(id, true, null, null);
                return;
            }

            if (!_handlers.TryGetValue(method, out var handler))
            {
                Respond(id, false, null, $"Método desconhecido: {method}");
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await handler(args).ConfigureAwait(false);
                    Respond(id, true, result, null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Bridge {Method} falhou", method);
                    Respond(id, false, null, ex.Message);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mensagem bridge inválida: {Raw}", raw);
        }
    }

    /// <summary>Push de evento assíncrono para o JS (sem id de request).</summary>
    public void SendEvent(string eventName, object? payload)
    {
        // Startup notifications have no receiver yet. The WebView reads the
        // current state when it mounts.
        if (Volatile.Read(ref _hasWebViewConnection))
            Send(new { @event = eventName, payload });
    }

    private void Respond(long? id, bool ok, object? result, string? error) =>
        Send(new { id, ok, result, error });

    private void Send(object message)
    {
        try
        {
            _window.SendWebMessage(JsonSerializer.Serialize(message, _jsonOptions));
        }
        catch (Exception ex)
        {
            // Resposta descartada em silêncio = invoke() pendente no JS. Warning para
            // aparecer no log — o timeout do cliente cobre o travamento.
            _logger.LogWarning(ex, "Falha ao enviar mensagem bridge ao JS");
        }
    }
}
