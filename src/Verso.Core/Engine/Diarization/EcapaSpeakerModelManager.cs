using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Verso.Core.Engine;

namespace Verso.Core.Engine.Diarization;

public sealed class EcapaSpeakerModelManager
{
    public const string FileName = "ecapa_tdnn.onnx";
    public const string DirectoryName = "ecapa-tdnn-onnx";
    public const string DisplayName = "ECAPA-TDNN (locutores)";
    public const string SizeLabel = "~80 MB";
    public const long MinimumBytes = 50_000_000;

    public const string Revision = "04c3ffe4fd00b3b7853fd57db44e2e531d4817f2";
    public const string DownloadUrl =
        "https://huggingface.co/pranjal-pravesh/ecapa_tdnn_onnx/resolve/" + Revision + "/" + FileName;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<HttpClient> DownloadHttpClient = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromHours(1) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Verso", "1.0"));
        return client;
    });

    private readonly ILogger<EcapaSpeakerModelManager>? _logger;
    private readonly IModelDownloadNotifier? _downloadNotifier;
    private readonly HttpClient _httpClient;

    public EcapaSpeakerModelManager(
        ILogger<EcapaSpeakerModelManager>? logger = null,
        IModelDownloadNotifier? downloadNotifier = null,
        HttpClient? httpClient = null)
    {
        _logger = logger;
        _downloadNotifier = downloadNotifier;
        _httpClient = httpClient ?? DownloadHttpClient.Value;
    }

    public static string GetModelPath(string modelsDirectory) =>
        Path.Combine(modelsDirectory, DirectoryName, FileName);

    public static bool IsModelValid(string modelPath) =>
        File.Exists(modelPath) && new FileInfo(modelPath).Length >= MinimumBytes;

    public async Task EnsureModelAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        if (IsModelValid(modelPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(modelPath)!;
        Directory.CreateDirectory(directory);
        var downloadLock = DownloadLocks.GetOrAdd(directory, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken);
        try
        {
            if (IsModelValid(modelPath))
            {
                return;
            }

            _downloadNotifier?.DownloadStarted(
                DisplayName,
                $"O modelo {DisplayName} ({SizeLabel}) está sendo baixado. " +
                "Isso ocorre apenas na primeira transcrição com identificação de locutores.");

            try
            {
                await DownloadFileAsync(DownloadUrl, modelPath, cancellationToken);
                if (!IsModelValid(modelPath))
                {
                    throw new InvalidOperationException($"Download de {DisplayName} incompleto em {modelPath}.");
                }
            }
            finally
            {
                _downloadNotifier?.DownloadCompleted();
            }
        }
        finally
        {
            downloadLock.Release();
        }
    }

    private async Task DownloadFileAsync(string url, string destPath, CancellationToken cancellationToken)
    {
        var tempPath = destPath + ".download";
        _logger?.LogInformation("Baixando {Url} → {Path}", url, destPath);

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var file = File.Create(tempPath))
        {
            await stream.CopyToAsync(file, cancellationToken);
            await file.FlushAsync(cancellationToken);
        }

        if (File.Exists(destPath))
        {
            File.Delete(destPath);
        }

        File.Move(tempPath, destPath);
    }
}
