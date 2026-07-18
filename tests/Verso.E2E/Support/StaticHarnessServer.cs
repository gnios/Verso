using System.Net;
using System.Text;

namespace Verso.E2E.Support;

/// <summary>Serve o HTML do harness em http://127.0.0.1:{port}/.</summary>
public sealed class StaticHarnessServer : IAsyncDisposable
{
    private readonly string _wwwRoot;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public StaticHarnessServer(string wwwRoot)
    {
        _wwwRoot = wwwRoot;
    }

    public string BaseUrl { get; private set; } = "";

    public void Start()
    {
        for (var i = 0; i < 10; i++)
        {
            var port = 19_100 + i;
            var prefix = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
                _listener = listener;
                BaseUrl = prefix;
                _cts = new CancellationTokenSource();
                _loop = Task.Run(() => LoopAsync(_cts.Token));
                return;
            }
            catch
            {
                listener.Close();
            }
        }

        throw new InvalidOperationException("Não foi possível iniciar StaticHarnessServer.");
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => Serve(ctx), CancellationToken.None);
        }
    }

    private void Serve(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath.Trim('/') ?? "";
            if (string.IsNullOrEmpty(path))
            {
                path = "audio-probe.html";
            }

            var file = Path.Combine(_wwwRoot, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            var bytes = File.ReadAllBytes(file);
            ctx.Response.ContentType = file.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                ? "text/html; charset=utf-8"
                : "application/octet-stream";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes);
            ctx.Response.Close();
        }
        catch
        {
            try { ctx.Response.Abort(); } catch { /* ignore */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        try { _listener?.Stop(); } catch { /* ignore */ }
        _listener?.Close();
        if (_loop is not null)
        {
            try { await _loop; } catch { /* ignore */ }
        }

        _cts?.Dispose();
    }
}
