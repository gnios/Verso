using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Verso.Core.Engine;

namespace Verso.App.Services;

/// <summary>
/// Serve arquivos de mídia em <c>http://127.0.0.1:{port}/</c> com suporte a
/// <c>Range</c>. Substitui o scheme <c>versomedia://</c> do Photino, que materializa
/// o stream inteiro em memória (<c>IntPtr</c> + <c>numBytes</c>) — inviável para áudio grande.
/// </summary>
public sealed class LocalMediaServer : IAsyncDisposable
{
    private readonly ILogger<LocalMediaServer> _logger;
    private readonly AudioLoader? _audioLoader;
    private readonly object _sync = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string _baseUrl = "";
    private long _bytesServed;
    private int _requestCount;
    private int _rangeRequestCount;
    // Picos: cache por arquivo+mtime e semáforo global — cada compute roda um ffmpeg;
    // uma rajada de requests não pode criar dezenas de processos e afogar o servidor.
    private readonly SemaphoreSlim _peaksGate = new(1);
    private readonly Dictionary<string, (long WriteTimeUtcTicks, int Buckets, float[] Peaks)> _peaksCache = new();

    public LocalMediaServer(ILogger<LocalMediaServer> logger, AudioLoader? audioLoader = null)
    {
        _logger = logger;
        _audioLoader = audioLoader;
    }

    /// <summary>Total de bytes escritos nas respostas (útil para asserts E2E de streaming).</summary>
    public long BytesServed => Interlocked.Read(ref _bytesServed);

    public int RequestCount => Volatile.Read(ref _requestCount);

    public int RangeRequestCount => Volatile.Read(ref _rangeRequestCount);

    public void ResetCounters()
    {
        Interlocked.Exchange(ref _bytesServed, 0);
        Volatile.Write(ref _requestCount, 0);
        Volatile.Write(ref _rangeRequestCount, 0);
    }

    public string BaseUrl
    {
        get
        {
            lock (_sync)
            {
                return _baseUrl;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _listener?.IsListening == true;
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_listener is not null)
            {
                return;
            }

            Exception? lastError = null;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var port = 17_890 + attempt;
                var prefix = $"http://127.0.0.1:{port}/";
                var listener = new HttpListener();
                listener.Prefixes.Add(prefix);
                try
                {
                    listener.Start();
                    _listener = listener;
                    _baseUrl = prefix;
                    _cts = new CancellationTokenSource();
                    _loop = Task.Run(() => ListenLoopAsync(_cts.Token));
                    _logger.LogInformation("LocalMediaServer em {BaseUrl}", _baseUrl);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    try
                    {
                        listener.Close();
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            throw new InvalidOperationException(
                "Não foi possível iniciar o LocalMediaServer em 127.0.0.1.",
                lastError);
        }
    }

    public string BuildUrl(string absoluteFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);
        var baseUrl = BaseUrl;
        if (string.IsNullOrEmpty(baseUrl))
        {
            throw new InvalidOperationException("LocalMediaServer ainda não foi iniciado.");
        }

        var escaped = Uri.EscapeDataString(Path.GetFullPath(absoluteFilePath));
        return $"{baseUrl}?path={escaped}";
    }

    public async ValueTask DisposeAsync()
    {
        HttpListener? listener;
        CancellationTokenSource? cts;
        Task? loop;
        lock (_sync)
        {
            listener = _listener;
            cts = _cts;
            loop = _loop;
            _listener = null;
            _cts = null;
            _loop = null;
            _baseUrl = "";
        }

        if (cts is not null)
        {
            await cts.CancelAsync();
        }

        try
        {
            listener?.Stop();
        }
        catch
        {
            // ignore
        }

        listener?.Close();

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        cts?.Dispose();
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListener? listener;
            lock (_sync)
            {
                listener = _listener;
            }

            if (listener is null || !listener.IsListening)
            {
                break;
            }

            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Erro transitório do listener: se ainda estiver escutando, continua —
                // sair do loop matava o servidor de mídia e o player parava de funcionar.
                if (listener.IsListening)
                {
                    _logger.LogWarning(ex, "Falha transitória no LocalMediaServer; continuando");
                    continue;
                }

                break;
            }

            _ = Task.Run(() => HandleRequest(context), CancellationToken.None);
        }
    }

    private void HandlePeaksRequest(HttpListenerContext context, string filePath, int buckets, string query)
    {
        var response = context.Response;
        response.ContentType = "application/json; charset=utf-8";
        response.AddHeader("Cache-Control", "no-store");

        try
        {
            if (_audioLoader is null)
            {
                response.StatusCode = (int)HttpStatusCode.NotImplemented;
                WriteJson(response, """{"error":"peaks indisponível"}""");
                return;
            }

            var durationSeconds = MediaSchemeHandler.GetQueryValue(query, "duration") is { } durationValue
                                  && double.TryParse(durationValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;

            var stamp = new FileInfo(filePath).LastWriteTimeUtc.Ticks;
            lock (_peaksCache)
            {
                if (_peaksCache.TryGetValue(filePath, out var cached)
                    && cached.WriteTimeUtcTicks == stamp
                    && cached.Buckets == buckets)
                {
                    WriteJson(response, JsonSerializer.Serialize(new { peaks = cached.Peaks }));
                    return;
                }
            }

            _peaksGate.Wait();
            try
            {
                var peaks = _audioLoader.ComputePeaks(filePath, Math.Min(buckets, 4096), durationSeconds);
                lock (_peaksCache)
                {
                    if (_peaksCache.Count >= 32)
                    {
                        _peaksCache.Clear();
                    }

                    _peaksCache[filePath] = (stamp, buckets, peaks);
                }

                WriteJson(response, JsonSerializer.Serialize(new { peaks }));
            }
            finally
            {
                _peaksGate.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao gerar picos de {FilePath}", filePath);
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
            WriteJson(response, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    private static void WriteJson(HttpListenerResponse response, string json)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Flush();
        response.Close();
    }

    private void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            // Preflight / CORS — o WebView pode tratar o origin do app como distinto de 127.0.0.1.
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, HEAD, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Range");
            response.AddHeader("Access-Control-Expose-Headers", "Content-Length, Content-Range, Accept-Ranges");

            if (HttpMethod.Options.Method.Equals(request.HttpMethod, StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = (int)HttpStatusCode.NoContent;
                response.Close();
                return;
            }

            if (!HttpMethod.Get.Method.Equals(request.HttpMethod, StringComparison.OrdinalIgnoreCase) &&
                !HttpMethod.Head.Method.Equals(request.HttpMethod, StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                response.Close();
                return;
            }

            var url = request.Url?.ToString() ?? "";
            // Reusa a validação de path do scheme antigo (mesma query ?path=).
            var schemeUrl = $"versomedia://local/{request.Url?.Query}";
            if (!MediaSchemeHandler.TryResolveMediaPath(schemeUrl, out var filePath) || !File.Exists(filePath))
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.Close();
                return;
            }

            Interlocked.Increment(ref _requestCount);

            // ?peaks=N&duration=S — picos do waveform calculados no servidor via
            // ffmpeg (streaming s16 8kHz, memória constante). Decodificar no renderer
            // estourava a memória do WebView2 em mídias longas (OOM → tela branca).
            var query = request.Url?.Query ?? "";
            if (MediaSchemeHandler.GetQueryValue(query, "peaks") is { } peaksValue
                && int.TryParse(peaksValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var buckets)
                && buckets > 0)
            {
                HandlePeaksRequest(context, filePath, buckets, query);
                return;
            }

            var contentType = MediaSchemeHandler.GetContentType(filePath);
            var fileInfo = new FileInfo(filePath);
            var totalLength = fileInfo.Length;

            response.AddHeader("Accept-Ranges", "bytes");
            response.ContentType = contentType;

            long start = 0;
            long end = totalLength - 1;
            var isPartial = false;
            var rangeHeader = request.Headers["Range"];
            if (!string.IsNullOrEmpty(rangeHeader) &&
                rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) &&
                totalLength > 0)
            {
                Interlocked.Increment(ref _rangeRequestCount);
                var spec = rangeHeader["bytes=".Length..].Trim();
                var dash = spec.IndexOf('-');
                if (dash >= 0)
                {
                    var startText = spec[..dash];
                    var endText = spec[(dash + 1)..];
                    if (long.TryParse(startText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStart))
                    {
                        start = parsedStart;
                    }

                    if (long.TryParse(endText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedEnd))
                    {
                        end = parsedEnd;
                    }

                    start = Math.Clamp(start, 0, Math.Max(0, totalLength - 1));
                    end = Math.Clamp(end, start, totalLength - 1);
                    isPartial = true;
                }
            }

            var contentLength = totalLength == 0 ? 0 : end - start + 1;
            response.ContentLength64 = contentLength;

            if (isPartial)
            {
                response.StatusCode = (int)HttpStatusCode.PartialContent;
                response.AddHeader(
                    "Content-Range",
                    $"bytes {start}-{end}/{totalLength.ToString(CultureInfo.InvariantCulture)}");
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.OK;
            }

            if (HttpMethod.Head.Method.Equals(request.HttpMethod, StringComparison.OrdinalIgnoreCase) ||
                contentLength == 0)
            {
                response.Close();
                return;
            }

            using var file = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (start > 0)
            {
                file.Seek(start, SeekOrigin.Begin);
            }

            var buffer = new byte[64 * 1024];
            var remaining = contentLength;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = file.Read(buffer, 0, toRead);
                if (read <= 0)
                {
                    break;
                }

                response.OutputStream.Write(buffer, 0, read);
                Interlocked.Add(ref _bytesServed, read);
                remaining -= read;
            }

            response.OutputStream.Flush();
            response.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao servir mídia local");
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.Close();
            }
            catch
            {
                // ignore
            }
        }
    }
}
