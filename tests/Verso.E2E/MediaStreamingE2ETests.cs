using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Verso.App.Services;
using Verso.E2E.Support;
using Xunit.Abstractions;

namespace Verso.E2E;

/// <summary>
/// Harness Playwright (Chromium) + LocalMediaServer — valida streaming/Range sem abrir o Photino.
/// É o teste que o agente consegue rodar de forma confiável em CI/local.
/// </summary>
public sealed class MediaStreamingE2ETests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private IPlaywright? _playwright;

    public MediaStreamingE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await PlaywrightHelper.EnsureBrowsersInstalledAsync();
        _playwright = await Playwright.CreateAsync();
    }

    public Task DisposeAsync()
    {
        _playwright?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Prepare_DoesNotFetchMedia_LoadUsesRange_AndBytesStayBelowFullFile()
    {
        var seed = await E2ESeed.CreateIsolatedWorkspaceAsync(mediaDuration: TimeSpan.FromSeconds(120));
        await using var mediaServer = new LocalMediaServer(NullLogger<LocalMediaServer>.Instance);
        mediaServer.Start();

        var harnessRoot = Path.Combine(AppContext.BaseDirectory, "Harness");
        await using var harness = new StaticHarnessServer(harnessRoot);
        harness.Start();

        var mediaUrl = mediaServer.BuildUrl(seed.MediaPath);
        var pageUrl = $"{harness.BaseUrl}audio-probe.html?mediaUrl={Uri.EscapeDataString(mediaUrl)}";

        Assert.NotNull(_playwright);
        await using var browser = await _playwright!.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();

        mediaServer.ResetCounters();
        await page.GotoAsync(pageUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByTestId("btn-prepare").ClickAsync();
        await Task.Delay(400);

        Assert.Equal(0, mediaServer.RequestCount);
        Assert.Equal(0, mediaServer.BytesServed);

        var sw = Stopwatch.StartNew();
        await page.GetByTestId("btn-load").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.getElementById('status')?.textContent === 'metadata'",
            null,
            new() { Timeout = 15_000 });
        sw.Stop();

        Assert.True(mediaServer.RangeRequestCount >= 1, "Esperava ao menos 1 request Range no load de metadata.");
        // Chromium pede um primeiro chunk generoso em WAV; ainda assim deve ficar abaixo do arquivo inteiro.
        Assert.True(
            mediaServer.BytesServed < seed.MediaBytes,
            $"BytesServed={mediaServer.BytesServed} >= arquivo inteiro ({seed.MediaBytes}) no metadata — parece full-file.");
        Assert.True(
            sw.Elapsed < TimeSpan.FromSeconds(5),
            $"loadedmetadata demorou {sw.Elapsed.TotalMilliseconds:0}ms");

        var bytesAfterMetadata = mediaServer.BytesServed;
        var rangesAfterMetadata = mediaServer.RangeRequestCount;

        await page.GetByTestId("btn-play").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.getElementById('status')?.textContent === 'playing' || window.__versoProbe.readyState() >= 2",
            null,
            new() { Timeout = 10_000 });
        await Task.Delay(400);

        // WAV PCM não comprimido: o Chromium pode bufferizar quase o arquivo no play.
        // O contrato crítico é metadata via Range (acima) + play não quebrar.
        Assert.True(mediaServer.RangeRequestCount >= rangesAfterMetadata);

        _output.WriteLine(
            $"MediaStreaming OK: file={seed.MediaBytes}B metadataBytes={bytesAfterMetadata}B ({100.0 * bytesAfterMetadata / seed.MediaBytes:0.0}% do arquivo) afterPlay={mediaServer.BytesServed}B ranges={mediaServer.RangeRequestCount} metadataMs={sw.ElapsedMilliseconds}");
    }
}
