using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Verso.Core.Services;

/// <summary>
/// Envia feedbacks para uma Google Sheets Web App (Google Apps Script).
///
/// ## Setup para o desenvolvedor
/// 1. Crie uma planilha no Google Sheets com as colunas: Timestamp, Tipo, Título, Descrição, Origem
/// 2. Extensões → Apps Script → cole o script abaixo → Implantar → Web app ("Anyone")
/// 3. Copie a URL gerada e cole na constante <see cref="WebAppUrl"/>.
///
/// ## Apps Script (cole no editor vinculado à planilha)
/// <code>
/// function doPost(e) {
///   var data = JSON.parse(e.postData.contents);
///   var sheet = SpreadsheetApp.getActiveSpreadsheet().getActiveSheet();
///   sheet.appendRow([new Date(), data.type, data.title, data.description, data.source]);
///   return ContentService.createTextOutput(
///     JSON.stringify({ status: 'ok' })
///   ).setMimeType(ContentService.MimeType.JSON);
/// }
/// </code>
/// </summary>
public sealed class FeedbackService
{
    // ── EDITE AQUI antes de compilar a release ──────────────────────
    // Após implantar o Apps Script como Web App ("Anyone"), cole a URL abaixo.
    // Ex: "https://script.google.com/macros/s/AKfycbw.../exec"
    private const string WebAppUrl = "https://script.google.com/macros/s/AKfycbzI5yqaV5sKH3cT5kO46DYTvZTLDKRNSOCs19MgHyYUPBRVtjlpRB_vb_2Hc6kLZoaenA/exec";
    // ────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;

    public FeedbackService(HttpClient http)
    {
        _http = http;
    }

    public bool IsConfigured =>
        WebAppUrl is { Length: > 20 }
        && WebAppUrl.StartsWith("https://script.google.com/macros/s/")
        && !WebAppUrl.Contains("seu-id-aqui");

    /// <summary>
    /// Envia o feedback para a planilha Google Sheets via Web App.
    /// </summary>
    /// <param name="type">"bug" ou "melhoria" — define qual aba recebe os dados.</param>
    /// <param name="title">Título resumido.</param>
    /// <param name="description">Descrição detalhada.</param>
    /// <returns>Resultado do envio.</returns>
    public async Task<FeedbackResult> SendAsync(
        string type, string title, string description,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return FeedbackResult.Failure(
                "Endpoint Google Sheets não configurado. " +
                "O desenvolvedor precisa criar o Web App e definir a URL em FeedbackService.cs.");
        }

        if (string.IsNullOrWhiteSpace(title))
            return FeedbackResult.Failure("Título é obrigatório.");

        using var request = new HttpRequestMessage(HttpMethod.Post, WebAppUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                type,
                title,
                description,
                source = "Verso Desktop",
            }, JsonOptions), Encoding.UTF8, "application/json"),
        };

        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Verso", "1.0"));

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);

            if (response.IsSuccessStatusCode)
                return FeedbackResult.Success();

            var body = await response.Content.ReadAsStringAsync(ct);
            return FeedbackResult.Failure(
                $"Serviço retornou {(int)response.StatusCode}: {Truncate(body)}");
        }
        catch (HttpRequestException ex)
        {
            return FeedbackResult.Failure($"Falha de rede: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return FeedbackResult.Failure("Requisição cancelada (timeout).");
        }
    }

    private static string Truncate(string s, int max = 200) =>
        s.Length <= max ? s : s[..max] + "…";
}

public sealed record FeedbackResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }

    public static FeedbackResult Success() => new() { IsSuccess = true };
    public static FeedbackResult Failure(string error) => new() { IsSuccess = false, ErrorMessage = error };
}
