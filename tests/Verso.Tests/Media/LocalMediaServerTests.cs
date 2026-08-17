using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Verso.App.Services;
using Verso.Core;

namespace Verso.Tests.Media;

public class LocalMediaServerTests
{
    [Fact]
    public async Task ServesMediaFile_WithRangeSupport()
    {
        var mediaRoot = Path.GetFullPath(VersoPaths.MediaDirectory);
        var filePath = Path.Combine(mediaRoot, Guid.NewGuid().ToString("N"), "clip.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var payload = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(filePath, payload);

        await using var server = new LocalMediaServer(NullLogger<LocalMediaServer>.Instance);
        server.Start();

        try
        {
            var url = server.BuildUrl(filePath);
            Assert.StartsWith("http://127.0.0.1:", url);

            using var client = new HttpClient();
            using var full = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, full.StatusCode);
            var fullBytes = await full.Content.ReadAsByteArrayAsync();
            Assert.Equal(payload, fullBytes);

            using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, url);
            rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=10-19");
            using var partial = await client.SendAsync(rangeRequest);
            Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
            var slice = await partial.Content.ReadAsByteArrayAsync();
            Assert.Equal(payload.AsSpan(10, 10).ToArray(), slice);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            var dir = Path.GetDirectoryName(filePath);
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RejectsPathOutsideMediaDirectory()
    {
        await using var server = new LocalMediaServer(NullLogger<LocalMediaServer>.Instance);
        server.Start();

        var outside = Path.Combine(Path.GetTempPath(), $"verso-outside-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(outside, [1, 2, 3]);

        try
        {
            var url = $"{server.BaseUrl}?path={Uri.EscapeDataString(outside)}";
            using var client = new HttpClient();
            using var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            if (File.Exists(outside))
            {
                File.Delete(outside);
            }
        }
    }
}
