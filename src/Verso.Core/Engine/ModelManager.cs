using System.Buffers.Binary;

using System.Collections.Concurrent;

using System.Net.Http.Headers;

using Microsoft.Extensions.Logging;

using Verso.Core;

using Verso.Core.Data.Entities;

using Whisper.net;

using Whisper.net.Ggml;



namespace Verso.Core.Engine;



public sealed class ModelManager

{

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<HttpClient> DownloadHttpClient = new(() =>

    {

        var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Verso", "1.0"));

        return client;

    });

    // Modelo distil-whisper-large-v3 fine-tuned para pt-BR (GGML Q5_0), hospedado no HuggingFace.
    // Não tem GgmlType canônico no Whisper.net — baixado por URL direta fora do WhisperGgmlDownloader.
    private const string PtBrTurboFileName = "ggml-distil-large-v3-ptbr-q5_0.bin";
    private const string PtBrTurboUrl =
        "https://huggingface.co/lucasparis1103/distil-whisper-large-v3-ptbr-ggml/resolve/main/ggml-distil-large-v3-ptbr-q5_0.bin";
    private const long PtBrTurboMinSizeBytes = 512_000_000;



    private readonly ILogger<ModelManager>? _logger;

    private readonly IModelDownloadNotifier? _downloadNotifier;

    private readonly IWhisperFactoryCache? _factoryCache;



    public ModelManager(

        ILogger<ModelManager>? logger = null,

        IModelDownloadNotifier? downloadNotifier = null,

        IWhisperFactoryCache? factoryCache = null)

    {

        _logger = logger;

        _downloadNotifier = downloadNotifier;

        _factoryCache = factoryCache;

    }



    public static string GetModelFileName(GgmlType type) => type switch
    {
        GgmlType.Tiny => "ggml-tiny.bin",
        GgmlType.TinyEn => "ggml-tiny.en.bin",
        GgmlType.Base => "ggml-base.bin",
        GgmlType.BaseEn => "ggml-base.en.bin",
        GgmlType.Small => "ggml-small.bin",
        GgmlType.SmallEn => "ggml-small.en.bin",
        GgmlType.Medium => "ggml-medium.bin",
        GgmlType.MediumEn => "ggml-medium.en.bin",
        GgmlType.LargeV1 => "ggml-large-v1.bin",
        GgmlType.LargeV2 => "ggml-large-v2.bin",
        GgmlType.LargeV3 => "ggml-large-v3.bin",
        GgmlType.LargeV3Turbo => "ggml-large-v3-turbo.bin",
        _ => $"ggml-{type.ToString().ToLowerInvariant()}.bin",
    };



    public static GgmlType MapQualityToGgmlType(ModelQuality quality) => quality switch
    {
        ModelQuality.Standard => GgmlType.Small,
        ModelQuality.High => GgmlType.LargeV3,
        ModelQuality.Tiny => GgmlType.Tiny,
        ModelQuality.Base => GgmlType.Base,
        ModelQuality.Medium => GgmlType.Medium,
        ModelQuality.LargeV2 => GgmlType.LargeV2,
        ModelQuality.LargeV3Turbo => GgmlType.LargeV3Turbo,
        ModelQuality.LargeV1 => GgmlType.LargeV1,
        ModelQuality.TinyEn => GgmlType.TinyEn,
        ModelQuality.BaseEn => GgmlType.BaseEn,
        ModelQuality.SmallEn => GgmlType.SmallEn,
        ModelQuality.MediumEn => GgmlType.MediumEn,
        _ => GgmlType.Small,
    };

    public static string GetModelFileName(ModelQuality quality) =>
        quality == ModelQuality.PtBrTurbo ? PtBrTurboFileName : GetModelFileName(MapQualityToGgmlType(quality));


    public static long GetMinimumModelSizeBytes(GgmlType ggmlType) => ggmlType switch
    {
        GgmlType.Tiny => 50_000_000,
        GgmlType.TinyEn => 50_000_000,
        GgmlType.Base => 100_000_000,
        GgmlType.BaseEn => 100_000_000,
        GgmlType.Small => 400_000_000,
        GgmlType.SmallEn => 400_000_000,
        GgmlType.Medium => 1_200_000_000,
        GgmlType.MediumEn => 1_200_000_000,
        GgmlType.LargeV1 => 2_500_000_000,
        GgmlType.LargeV2 => 2_500_000_000,
        GgmlType.LargeV3 => 2_500_000_000,
        GgmlType.LargeV3Turbo => 1_200_000_000,
        _ => 10_000_000,
    };



    private const uint GgmlMagicLittleEndian = 0x67676d6c;

    private const uint GgufMagicLittleEndian = 0x46554747;



    public static bool HasGgmlMagic(string path)

    {

        if (!File.Exists(path))

        {

            return false;

        }



        Span<byte> header = stackalloc byte[4];

        using var stream = File.OpenRead(path);

        if (stream.Read(header) != 4)

        {

            return false;

        }



        var magic = BinaryPrimitives.ReadUInt32LittleEndian(header);

        return magic is GgmlMagicLittleEndian or GgufMagicLittleEndian;

    }



    public static string DescribeModelMagic(string path)

    {

        if (!File.Exists(path))

        {

            return "arquivo ausente";

        }



        Span<byte> header = stackalloc byte[4];

        using var stream = File.OpenRead(path);

        if (stream.Read(header) != 4)

        {

            return "header incompleto";

        }



        var magic = BinaryPrimitives.ReadUInt32LittleEndian(header);

        return magic switch

        {

            GgmlMagicLittleEndian => "GGML",

            GgufMagicLittleEndian => "GGUF",

            _ => $"desconhecido (0x{magic:X8})",

        };

    }



    public static bool IsModelFileValid(string modelPath, GgmlType ggmlType)

    {

        if (!File.Exists(modelPath))

        {

            return false;

        }



        var size = new FileInfo(modelPath).Length;

        if (size < GetMinimumModelSizeBytes(ggmlType))

        {

            return false;

        }



        return HasGgmlMagic(modelPath);

    }



    public Task EnsureModelAsync(string modelPath, GgmlType ggmlType, CancellationToken cancellationToken = default) =>

        EnsureModelInternalAsync(modelPath, ggmlType, cancellationToken);



    public Task EnsureModelAsync(string modelPath, ModelQuality quality, CancellationToken cancellationToken = default) =>
        quality == ModelQuality.PtBrTurbo
            ? EnsureCustomModelAsync(modelPath, quality, cancellationToken)
            : EnsureModelInternalAsync(modelPath, MapQualityToGgmlType(quality), cancellationToken, quality);

    /// <summary>
    /// Validação por ModelQuality — usa o tamanho mínimo do GgmlType canônico, exceto para o
    /// modelo pt-BR customizado (sem GgmlType), que tem tamanho mínimo próprio.
    /// </summary>
    public static bool IsModelFileValid(string modelPath, ModelQuality quality)
    {
        if (!File.Exists(modelPath))
        {
            return false;
        }

        var minSize = quality == ModelQuality.PtBrTurbo
            ? PtBrTurboMinSizeBytes
            : GetMinimumModelSizeBytes(MapQualityToGgmlType(quality));

        return new FileInfo(modelPath).Length >= minSize && HasGgmlMagic(modelPath);
    }



    private async Task EnsureModelInternalAsync(

        string modelPath,

        GgmlType ggmlType,

        CancellationToken cancellationToken,

        ModelQuality? qualityForUi = null)

    {

        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);



        if (IsModelFileValid(modelPath, ggmlType))

        {

            _logger?.LogInformation("Modelo já disponível em {ModelPath} ({SizeBytes} bytes)", modelPath, new FileInfo(modelPath).Length);

            return;

        }

        _logger?.LogInformation(
            "Modelo não encontrado em {ModelPath} — baixando {GgmlType} (~{MinSizeMb} MB)",
            modelPath,
            ggmlType,
            GetMinimumModelSizeBytes(ggmlType) / 1_000_000);



        var downloadLock = DownloadLocks.GetOrAdd(modelPath, _ => new SemaphoreSlim(1, 1));

        await downloadLock.WaitAsync(cancellationToken);



        try

        {

            if (IsModelFileValid(modelPath, ggmlType))

            {

                return;

            }



            var tempPath = modelPath + ".download";



            if (File.Exists(modelPath) && !IsModelFileValid(modelPath, ggmlType))

            {

                _logger?.LogWarning(

                    "Modelo inválido em {ModelPath} ({SizeBytes} bytes). Será baixado novamente.",

                    modelPath,

                    new FileInfo(modelPath).Length);

                ReleaseModelFile(modelPath);

            }



            if (IsModelFileValid(tempPath, ggmlType))

            {

                _logger?.LogInformation("Finalizando download parcial em {TempPath}", tempPath);

                FinalizeDownload(tempPath, modelPath);

                return;

            }



            if (File.Exists(tempPath))

            {

                _logger?.LogWarning(

                    "Arquivo temporário incompleto em {TempPath} ({SizeBytes} bytes). Removendo.",

                    tempPath,

                    new FileInfo(tempPath).Length);

                ReleaseTempFile(tempPath);

            }



            _logger?.LogInformation("Baixando modelo {ModelType} via Whisper.net…", ggmlType);



            var notifyDownload = qualityForUi.HasValue;

            if (notifyDownload)

            {

                _downloadNotifier?.DownloadStarted(qualityForUi!.Value);

            }



            try

            {

                var downloader = new WhisperGgmlDownloader(DownloadHttpClient.Value);

                await using var modelStream = await downloader.GetGgmlModelAsync(ggmlType, cancellationToken: cancellationToken);

                await using (var fileWriter = File.Create(tempPath))

                {

                    await modelStream.CopyToAsync(fileWriter, cancellationToken);

                    await fileWriter.FlushAsync(cancellationToken);

                }



                var downloadedSize = new FileInfo(tempPath).Length;

                if (!IsModelFileValid(tempPath, ggmlType))

                {

                    var magicDescription = DescribeModelMagic(tempPath);

                    throw new InvalidOperationException(

                        $"Download do modelo {ggmlType} incompleto ou inválido ({downloadedSize} bytes, magic={magicDescription}). " +

                        $"Esperado pelo menos {GetMinimumModelSizeBytes(ggmlType)} bytes.");

                }



                FinalizeDownload(tempPath, modelPath);

                _logger?.LogInformation("Modelo salvo em {ModelPath} ({SizeBytes} bytes)", modelPath, downloadedSize);

            }

            catch (Exception ex)

            {

                _logger?.LogError(ex, "Falha ao baixar modelo {ModelType}", ggmlType);

                if (File.Exists(tempPath) && !IsModelFileValid(tempPath, ggmlType))

                {

                    ReleaseTempFile(tempPath);

                }



                throw;

            }

            finally

            {

                if (notifyDownload)

                {

                    _downloadNotifier?.DownloadCompleted();

                }

            }

        }

        finally

        {

            downloadLock.Release();

        }

    }

    /// <summary>
    /// Download de um modelo GGML de URL direta (fora do catálogo canônico do Whisper.net),
    /// usado hoje pelo distil-whisper-large-v3 pt-BR. Mesma estrutura de EnsureModelInternalAsync:
    /// lock por caminho, arquivo temporário, validação por tamanho+magic, notificação de UI.
    /// </summary>
    private async Task EnsureCustomModelAsync(string modelPath, ModelQuality quality, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);

        if (IsModelFileValid(modelPath, quality))
        {
            _logger?.LogInformation("Modelo pt-BR já disponível em {ModelPath} ({SizeBytes} bytes)", modelPath, new FileInfo(modelPath).Length);
            return;
        }

        _logger?.LogInformation(
            "Modelo pt-BR não encontrado em {ModelPath} — baixando de {Url}",
            modelPath,
            PtBrTurboUrl);

        var downloadLock = DownloadLocks.GetOrAdd(modelPath, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken);

        try
        {
            if (IsModelFileValid(modelPath, quality))
            {
                return;
            }

            var tempPath = modelPath + ".download";

            if (File.Exists(modelPath) && !IsModelFileValid(modelPath, quality))
            {
                _logger?.LogWarning("Modelo pt-BR inválido em {ModelPath}. Será baixado novamente.", modelPath);
                ReleaseModelFile(modelPath);
            }

            if (IsModelFileValid(tempPath, quality))
            {
                _logger?.LogInformation("Finalizando download parcial em {TempPath}", tempPath);
                FinalizeDownload(tempPath, modelPath);
                return;
            }

            if (File.Exists(tempPath))
            {
                _logger?.LogWarning("Arquivo temporário incompleto em {TempPath}. Removendo.", tempPath);
                ReleaseTempFile(tempPath);
            }

            var url = quality == ModelQuality.PtBrTurbo ? PtBrTurboUrl : throw new InvalidOperationException($"Modelo {quality} não tem URL de download customizado.");
            _logger?.LogInformation("Baixando modelo pt-BR de {Url}…", url);
            _downloadNotifier?.DownloadStarted(quality);

            try
            {
                using var response = await DownloadHttpClient.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var modelStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var fileWriter = File.Create(tempPath))
                {
                    await modelStream.CopyToAsync(fileWriter, cancellationToken);
                    await fileWriter.FlushAsync(cancellationToken);
                }

                var downloadedSize = new FileInfo(tempPath).Length;
                if (!IsModelFileValid(tempPath, quality))
                {
                    var magicDescription = DescribeModelMagic(tempPath);
                    throw new InvalidOperationException(
                        $"Download do modelo pt-BR incompleto ou inválido ({downloadedSize} bytes, magic={magicDescription}). " +
                        $"Esperado pelo menos {PtBrTurboMinSizeBytes} bytes.");
                }

                FinalizeDownload(tempPath, modelPath);
                _logger?.LogInformation("Modelo pt-BR salvo em {ModelPath} ({SizeBytes} bytes)", modelPath, downloadedSize);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Falha ao baixar modelo pt-BR de {Url}", url);
                if (File.Exists(tempPath) && !IsModelFileValid(tempPath, quality))
                {
                    ReleaseTempFile(tempPath);
                }

                throw;
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



    private void FinalizeDownload(string tempPath, string modelPath)

    {

        ReleaseModelFile(modelPath);

        FileAccessHelper.RunWithRetry(() =>

        {

            File.Copy(tempPath, modelPath, overwrite: true);

            File.Delete(tempPath);

        });

    }



    private void ReleaseModelFile(string path)

    {

        if (!File.Exists(path))

        {

            return;

        }



        _factoryCache?.Invalidate(path);

        FileAccessHelper.RunWithRetry(() => File.Delete(path));

    }



    private static void ReleaseTempFile(string path)

    {

        if (!File.Exists(path))

        {

            return;

        }



        FileAccessHelper.RunWithRetry(() => File.Delete(path));

    }

}


