using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Verso.App.Services;

/// <summary>
/// Servidor HTTP local que serve o build React do wwwroot — o Photino carrega a UI de
/// <c>http://127.0.0.1:{port}/</c>. Mesmo padrão do <see cref="LocalMediaServer"/>
/// (HttpListener + portas tentativas), mas com MIME por extensão e sem Range (assets estáticos).
/// A UI não depende de file:// (ES modules/CORS) nem de scheme custom do Photino (que
/// materializa streams em memória).
/// </summary>
public sealed class LocalAppServer : IAsyncDisposable
{
    private readonly ILogger<LocalAppServer> _logger;
    private readonly string _wwwroot;
    private readonly object _sync = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string _baseUrl = "";

    public LocalAppServer(ILogger<LocalAppServer> logger, string wwwroot)
    {
        _logger = logger;
        _wwwroot = wwwroot;
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
                var port = 18_090 + attempt;
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
                    _logger.LogInformation("LocalAppServer em {BaseUrl} (wwwroot: {Wwwroot})", _baseUrl, _wwwroot);
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
                "Não foi possível iniciar o LocalAppServer em 127.0.0.1.",
                lastError);
        }
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

    private void HandleRecordingUpload(HttpListenerContext context)
    {
        var response = context.Response;
        response.ContentType = "application/json; charset=utf-8";
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.AddHeader("Cache-Control", "no-store");

        try
        {
            if (context.Request.ContentLength64 is <= 0)
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                WriteJson(response, """{"error":"corpo vazio"}""");
                return;
            }

            // Temp: upload.start copia para o storage do app (data/media) via
            // MediaStorageService.CopyToStorageAsync antes de transcrever.
            var targetDir = Path.Combine(Path.GetTempPath(), "verso-recordings");
            Directory.CreateDirectory(targetDir);
            var path = Path.Combine(targetDir, $"verso-{DateTime.Now:yyyyMMdd-HHmmss}.webm");

            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                context.Request.InputStream.CopyTo(file);
            }

            _logger.LogInformation("Gravação salva em {Path} ({Bytes} bytes)", path, context.Request.ContentLength64);
            response.StatusCode = (int)HttpStatusCode.OK;
            WriteJson(response, JsonSerializer.Serialize(new { path }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao salvar gravação");
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
                // sair do loop aqui matava o servidor e toda navegação passava a dar
                // página em branco (assets 404/conexão recusada).
                if (listener.IsListening)
                {
                    _logger.LogWarning(ex, "Falha transitória no LocalAppServer; continuando");
                    continue;
                }

                break;
            }

            _ = Task.Run(() => HandleRequest(context), CancellationToken.None);
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            // POST /recordings: blob de áudio da gravação do microfone (MediaRecorder no React).
            // HTTP em vez da ponte de mensagens: blob não passa por JSON/base64.
            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                && (request.Url?.AbsolutePath ?? "/") == "/recordings")
            {
                HandleRecordingUpload(context);
                return;
            }

            if (!HttpMethod.Get.Method.Equals(request.HttpMethod, StringComparison.OrdinalIgnoreCase) &&
                !HttpMethod.Head.Method.Equals(request.HttpMethod, StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                response.Close();
                return;
            }

            var urlPath = request.Url?.AbsolutePath ?? "/";
            if (urlPath == "/" || string.IsNullOrEmpty(urlPath))
            {
                urlPath = "/index.html";
            }

            var fullRoot = Path.GetFullPath(_wwwroot).TrimEnd(Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(_wwwroot, urlPath.TrimStart('/')));
            if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath))
            {
                // SPA fallback: rota desconhecida serve o index.html (hash-based/state navigation).
                fullPath = Path.Combine(_wwwroot, "index.html");
                if (!File.Exists(fullPath))
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    response.Close();
                    return;
                }
            }

            response.ContentType = GetContentType(fullPath);
            // Sem cache para index.html (dev); assets hasheados pelo Vite podem cachear, mas a
            // simplicidade aqui é preferível — o app é desktop.
            response.AddHeader("Cache-Control", "no-cache");

            if (HttpMethod.Head.Method.Equals(request.HttpMethod, StringComparison.OrdinalIgnoreCase))
            {
                response.ContentLength64 = new FileInfo(fullPath).Length;
                response.Close();
                return;
            }

            var bytes = File.ReadAllBytes(fullPath);
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Flush();
            response.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao servir asset estático");
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

    private static string GetContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return MimeTypes.TryGetValue(extension, out var mime) ? mime : "application/octet-stream";
    }

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".wasm"] = "application/wasm",
        [".map"] = "application/json; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
    };
}