using System.Security.Cryptography;

namespace Verso.Core.Engine.Diarization;

public sealed class SherpaSpeakerModels(IModelDownloadNotifier? notifier = null)
{
    internal const string DirectoryName = "sherpa-speaker-diarization";
    internal const string SegmentationFileName = "pyannote-segmentation-3.0.onnx";
    internal const string EmbeddingFileName = "eres2net.onnx";
    private const string SegmentationUrl =
        "https://huggingface.co/csukuangfj/sherpa-onnx-pyannote-segmentation-3-0/resolve/main/model.onnx";
    private const string EmbeddingUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx";
    private const string SegmentationHash = "220AD67CA923BEF2FA91F2390C786097BF305BCEB5E261D4AF67B38E938E1079";
    private const string EmbeddingHash = "F1EE61BE04000B50392A63442E5DD9FEAEA7E81DDDD494D3647CF1C3D88A1498";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly SemaphoreSlim _downloadLock = new(1, 1);
    private bool _ready;

    private static string ModelDirectory => Path.Combine(VersoPaths.ModelsDirectory, DirectoryName);
    internal string SegmentationPath => Path.Combine(ModelDirectory, SegmentationFileName);
    internal string EmbeddingPath => Path.Combine(ModelDirectory, EmbeddingFileName);

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        await _downloadLock.WaitAsync(cancellationToken);
        try
        {
            if (_ready)
                return;

            var hasSegmentation = await HasHashAsync(SegmentationPath, SegmentationHash, cancellationToken);
            var hasEmbedding = await HasHashAsync(EmbeddingPath, EmbeddingHash, cancellationToken);
            if (!hasSegmentation || !hasEmbedding)
            {
                Directory.CreateDirectory(ModelDirectory);
                notifier?.DownloadStarted("Identificação de locutores (Beta)", "Sherpa ONNX · ~46 MB");
                try
                {
                    if (!hasSegmentation)
                        await DownloadAsync(SegmentationUrl, SegmentationPath, SegmentationHash, cancellationToken);
                    if (!hasEmbedding)
                        await DownloadAsync(EmbeddingUrl, EmbeddingPath, EmbeddingHash, cancellationToken);
                }
                finally
                {
                    notifier?.DownloadCompleted();
                }
            }

            _ready = true;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    private static async Task DownloadAsync(string url, string path, string hash, CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var file = File.Create(temporary))
                await response.Content.CopyToAsync(file, cancellationToken);
            if (!await HasHashAsync(temporary, hash, cancellationToken))
                throw new InvalidDataException($"Checksum inválido para {Path.GetFileName(path)}.");

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static async Task<bool> HasHashAsync(string path, string hash, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return false;

        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)) == hash;
    }
}
