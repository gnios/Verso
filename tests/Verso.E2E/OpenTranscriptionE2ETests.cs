using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Playwright;
using Verso.E2E.Support;
using Xunit.Abstractions;

namespace Verso.E2E;

/// <summary>
/// E2E real: sobe o Verso.App (Photino/WebView2), conecta Playwright via CDP e mede a abertura.
/// Windows-only (WebView2).
/// </summary>
public sealed class OpenTranscriptionE2ETests
{
    private readonly ITestOutputHelper _output;

    public OpenTranscriptionE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string FindAppExe()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Verso.App", "bin", "Debug", "net10.0", "Verso.App.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Verso.App", "bin", "Release", "net10.0", "Verso.App.exe")),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException(
            "Verso.App.exe não encontrado. Rode `dotnet build src/Verso.App` antes do E2E Photino.");
    }

    [Fact]
    public async Task OpenTranscription_ShowsEditorWithoutFetchingMedia_ThenPlayUsesHttpRange()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var seed = await E2ESeed.CreateIsolatedWorkspaceAsync(mediaDuration: TimeSpan.FromSeconds(90));
        var exe = FindAppExe();
        var cdpPort = 9222 + Random.Shared.Next(0, 200);
        var userData = Path.Combine(Path.GetTempPath(), "verso-e2e-wv2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(userData);

        await PlaywrightHelper.EnsureBrowsersInstalledAsync();

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        psi.Environment["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] = $"--remote-debugging-port={cdpPort}";
        psi.Environment["WEBVIEW2_USER_DATA_FOLDER"] = userData;
        psi.Environment["VERSO_DATA_ROOT"] = seed.DataRoot;

        using var app = Process.Start(psi)
            ?? throw new InvalidOperationException("Falha ao iniciar Verso.App.");

        IPlaywright? playwright = null;
        IBrowser? browser = null;
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
            IPage? page = null;
            Exception? last = null;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    playwright ??= await Playwright.CreateAsync();
                    if (browser is not null)
                    {
                        await browser.CloseAsync();
                    }

                    browser = await playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{cdpPort}");
                    var ctx = browser.Contexts.FirstOrDefault();
                    page = ctx?.Pages.FirstOrDefault();
                    if (page is not null)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (browser is not null)
                    {
                        try { await browser.CloseAsync(); } catch { /* ignore */ }
                    }

                    browser = null;
                }

                await Task.Delay(500);
            }

            Assert.True(page is not null, $"Não conectou ao WebView2 CDP: {last?.Message}");

            await page!.WaitForSelectorAsync(
                "[data-testid='transcription-card']",
                new() { Timeout = 30_000 });

            var mediaRequestCount = 0;
            var rangeRequestCount = 0;
            long mediaBytesHeader = 0;

            void OnRequest(object? _, IRequest req)
            {
                if (!IsMediaUrl(req.Url))
                {
                    return;
                }

                Interlocked.Increment(ref mediaRequestCount);
                if (req.Headers.TryGetValue("range", out var range) ||
                    req.Headers.TryGetValue("Range", out range))
                {
                    if (!string.IsNullOrEmpty(range))
                    {
                        Interlocked.Increment(ref rangeRequestCount);
                    }
                }
            }

            void OnResponse(object? _, IResponse res)
            {
                if (!IsMediaUrl(res.Url))
                {
                    return;
                }

                if (res.Headers.TryGetValue("content-length", out var cl) &&
                    long.TryParse(cl, out var n))
                {
                    Interlocked.Add(ref mediaBytesHeader, n);
                }
            }

            page.Request += OnRequest;
            page.Response += OnResponse;

            var sw = Stopwatch.StartNew();
            await page.GetByTestId("transcription-card").First.ClickAsync();

            await page.WaitForSelectorAsync(
                "[data-testid='editor-title']",
                new() { Timeout = 20_000 });
            await page.WaitForSelectorAsync(
                "[data-testid='transcript-segments']",
                new() { Timeout = 20_000 });
            sw.Stop();

            var title = await page.GetByTestId("editor-title").InputValueAsync();
            Assert.Contains("E2E", title, StringComparison.OrdinalIgnoreCase);

            var mediaAfterOpen = Volatile.Read(ref mediaRequestCount);
            Assert.Equal(0, mediaAfterOpen);

            Assert.True(
                sw.Elapsed < TimeSpan.FromSeconds(8),
                $"Abrir transcrição demorou {sw.Elapsed.TotalMilliseconds:0}ms (limite 8s).");

            await page.GetByTestId("player-play").ClickAsync();
            await Task.Delay(1500);

            var mediaAfterPlay = Volatile.Read(ref mediaRequestCount);
            var ranges = Volatile.Read(ref rangeRequestCount);
            var bytes = Interlocked.Read(ref mediaBytesHeader);

            Assert.True(mediaAfterPlay >= 1, "Play deveria disparar fetch HTTP da mídia.");
            Assert.True(ranges >= 1, "Play deveria usar Range (streaming).");
            // WAV PCM: o WebView pode bufferizar o arquivo quase inteiro no play — ok.
            // O contrato crítico é lazy open (0 requests) + Range no play.

            _output.WriteLine(
                $"Photino E2E OK: openMs={sw.ElapsedMilliseconds} mediaAfterOpen={mediaAfterOpen} afterPlay={mediaAfterPlay} ranges={ranges} bytesHeader={bytes} file={seed.MediaBytes}");
        }
        finally
        {
            try
            {
                if (!app.HasExited)
                {
                    app.Kill(entireProcessTree: true);
                    app.WaitForExit(5_000);
                }
            }
            catch
            {
                // ignore
            }

            if (browser is not null)
            {
                try { await browser.CloseAsync(); } catch { /* ignore */ }
            }

            playwright?.Dispose();

            TryDelete(userData);
            TryDelete(seed.DataRoot);
        }
    }

    private static bool IsMediaUrl(string url) =>
        url.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
        url.Contains("path=", StringComparison.Ordinal);

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // ignore
        }
    }
}
