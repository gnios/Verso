using Transcriba.Core.Data.Entities;
using Whisper.net;
using Whisper.net.Ggml;

namespace Transcriba.Core.Engine;

public sealed class ModelManager
{
    public static string GetModelFileName(GgmlType type) => type switch
    {
        GgmlType.Tiny => "ggml-tiny.bin",
        GgmlType.Base => "ggml-base.bin",
        GgmlType.Small => "ggml-small.bin",
        GgmlType.Medium => "ggml-medium.bin",
        GgmlType.LargeV2 => "ggml-large-v2.bin",
        GgmlType.LargeV3 => "ggml-large-v3.bin",
        GgmlType.LargeV3Turbo => "ggml-large-v3-turbo.bin",
        _ => $"ggml-{type.ToString().ToLowerInvariant()}.bin",
    };

    public static GgmlType MapQualityToGgmlType(ModelQuality quality) => quality switch
    {
        ModelQuality.Standard => GgmlType.Small,
        ModelQuality.High => GgmlType.LargeV3,
        _ => GgmlType.Small,
    };

    public static string GetModelFileName(ModelQuality quality) =>
        GetModelFileName(MapQualityToGgmlType(quality));

    public async Task EnsureModelAsync(string modelPath, GgmlType ggmlType, CancellationToken cancellationToken = default)
    {
        if (File.Exists(modelPath))
            return;

        await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType);
        await using var fileWriter = File.OpenWrite(modelPath);
        await modelStream.CopyToAsync(fileWriter, cancellationToken);
    }

    public async Task EnsureModelAsync(string modelPath, ModelQuality quality, CancellationToken cancellationToken = default) =>
        await EnsureModelAsync(modelPath, MapQualityToGgmlType(quality), cancellationToken);
}
