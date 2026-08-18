using System.Text.Json;
using System.Text.Json.Serialization;

namespace Verso.Core.Update;

public sealed record UpdateChannel(string Variant, string Rid)
{
    public const string FileName = "verso-channel.json";

    public string AssetFileName(string version) => $"Verso-{version}-{Variant}-{Rid}.zip";

    public static UpdateChannel? TryLoad(string appDirectory)
    {
        if (string.IsNullOrWhiteSpace(appDirectory))
            return null;

        var path = Path.Combine(appDirectory, FileName);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize(json, UpdateChannelJsonContext.Default.UpdateChannelDto);
            if (dto is null
                || string.IsNullOrWhiteSpace(dto.Variant)
                || string.IsNullOrWhiteSpace(dto.Rid))
            {
                return null;
            }

            return new UpdateChannel(dto.Variant.Trim(), dto.Rid.Trim());
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal sealed record UpdateChannelDto(string? Variant, string? Rid);
}

[JsonSerializable(typeof(UpdateChannel.UpdateChannelDto))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class UpdateChannelJsonContext : JsonSerializerContext;
