using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Verso.Core.Data.Entities;

namespace Verso.Core.Engine;

public sealed class ParakeetModelManager
{
    public const string EncoderFileName = "encoder-model.int8.onnx";
    public const string DecoderJointFileName = "decoder_joint-model.int8.onnx";
    public const string PreprocessorFileName = "nemo128.onnx";
    public const string VocabFileName = "vocab.txt";

    public static readonly string[] RequiredFiles =
    [
        EncoderFileName,
        DecoderJointFileName,
        PreprocessorFileName,
        VocabFileName,
    ];

    private const string HfV3Base =
        "https://huggingface.co/istupakov/parakeet-tdt-0.6b-v3-onnx/resolve/main/";
    private const string HfTagarelaBase =
        "https://huggingface.co/calneymgp/parakeet-tdt-0.6b-v3-ptBR-TAGARELA-onnx-int8/resolve/main/";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<HttpClient> DownloadHttpClient = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Verso", "1.0"));
        return client;
    });

    private readonly ILogger<ParakeetModelManager>? _logger;
    private readonly IModelDownloadNotifier? _downloadNotifier;
    private readonly HttpClient _httpClient;

    public ParakeetModelManager(
        ILogger<ParakeetModelManager>? logger = null,
        IModelDownloadNotifier? downloadNotifier = null,
        HttpClient? httpClient = null)
    {
        _logger = logger;
        _downloadNotifier = downloadNotifier;
        _httpClient = httpClient ?? DownloadHttpClient.Value;
    }

    public static string GetModelDirectoryName(ParakeetModel model) => model switch
    {
        ParakeetModel.PtBrTagarela => "parakeet-ptbr-tagarela-int8",
        _ => "parakeet-tdt-0.6b-v3-int8",
    };

    public static string GetDisplayName(ParakeetModel model) => model switch
    {
        ParakeetModel.PtBrTagarela => "Parakeet pt-BR TAGARELA",
        _ => "Parakeet TDT v3 (multilíngue)",
    };

    public static string GetSizeLabel(ParakeetModel model) => model switch
    {
        ParakeetModel.PtBrTagarela => "~890 MB",
        _ => "~670 MB",
    };

    public static long GetMinimumEncoderBytes(ParakeetModel model) => model switch
    {
        ParakeetModel.PtBrTagarela => 500_000_000,
        _ => 400_000_000,
    };

    public static string GetBaseUrl(ParakeetModel model) =>
        model == ParakeetModel.PtBrTagarela ? HfTagarelaBase : HfV3Base;

    public static bool IsModelDirectoryValid(string directory, ParakeetModel model)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        foreach (var file in RequiredFiles)
        {
            var path = Path.Combine(directory, file);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                return false;
            }
        }

        var encoderPath = Path.Combine(directory, EncoderFileName);
        return new FileInfo(encoderPath).Length >= GetMinimumEncoderBytes(model);
    }

    public async Task EnsureModelAsync(string directory, ParakeetModel model, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        if (IsModelDirectoryValid(directory, model))
        {
            _logger?.LogInformation("Modelo Parakeet já disponível em {Directory}", directory);
            return;
        }

        var downloadLock = DownloadLocks.GetOrAdd(directory, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken);
        try
        {
            if (IsModelDirectoryValid(directory, model))
            {
                return;
            }

            _downloadNotifier?.DownloadStarted(
                GetDisplayName(model),
                $"O modelo {GetDisplayName(model)} ({GetSizeLabel(model)}) está sendo baixado. " +
                "Isso pode levar alguns minutos e ocorre apenas na primeira transcrição com este modelo.");

            try
            {
                var baseUrl = GetBaseUrl(model);
                foreach (var file in RequiredFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dest = Path.Combine(directory, file);
                    if (file == EncoderFileName)
                    {
                        if (File.Exists(dest) && new FileInfo(dest).Length >= GetMinimumEncoderBytes(model))
                        {
                            continue;
                        }
                    }
                    else if (File.Exists(dest) && new FileInfo(dest).Length > 0)
                    {
                        continue;
                    }

                    await DownloadFileAsync(baseUrl + file, dest, cancellationToken);
                }

                if (!IsModelDirectoryValid(directory, model))
                {
                    throw new InvalidOperationException(
                        $"Download do modelo {GetDisplayName(model)} incompleto em {directory}.");
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
