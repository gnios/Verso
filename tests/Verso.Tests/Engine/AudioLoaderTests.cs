using Verso.Core.Engine;

namespace Verso.Tests.Engine;

public class AudioLoaderTests
{
    [Fact]
    public void ConvertPcm16ToFloat_WithKnownPcmBytes_ProducesExpectedFloatSamples()
    {
        var pcmBytes = new byte[]
        {
            0x00, 0x00, // 0
            0x00, 0x40, // 16384 -> 0.5
            0x00, 0x80, // -32768 -> -1.0
            0xFF, 0x7F, // 32767 -> ~0.999969
        };

        var samples = AudioLoader.ConvertPcm16ToFloat(pcmBytes);

        Assert.Equal(4, samples.Length);
        Assert.Equal(0f, samples[0], precision: 6);
        Assert.Equal(0.5f, samples[1], precision: 6);
        Assert.Equal(-1f, samples[2], precision: 6);
        Assert.Equal(32767f / 32768f, samples[3], precision: 6);
    }

    [Fact]
    public void ConvertPcm16ToFloat_WithBufferSmallerThanTwoBytes_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AudioLoader.ConvertPcm16ToFloat(ReadOnlySpan<byte>.Empty));

        Assert.Equal("ffmpeg não retornou áudio.", ex.Message);
    }

    [Fact]
    public void BuildFfmpegPcmArguments_WithoutSeek_PlacesInputBeforeResample()
    {
        var args = AudioLoader.BuildFfmpegPcmArguments(@"C:\a.m4a", startSeconds: null, durationSeconds: null);

        Assert.Contains("-i \"C:\\a.m4a\"", args, StringComparison.Ordinal);
        Assert.DoesNotContain(" -ss ", args, StringComparison.Ordinal);
        Assert.Contains("-ar 16000 -ac 1 -f s16le pipe:1", args, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFfmpegPcmArguments_WithWindow_UsesAccurateHybridSeek()
    {
        var args = AudioLoader.BuildFfmpegPcmArguments(@"C:\a.m4a", startSeconds: 18, durationSeconds: 20);
        var preroll = AudioLoader.FfmpegSeekPrerollSeconds;
        var inputSeek = (18 - preroll).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var outputSeek = preroll.ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains($"-ss {inputSeek} -i \"C:\\a.m4a\" -ss {outputSeek} -t 20", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-t 20 -i ", args, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFfmpegPcmArguments_NearStart_DoesNotSeekBeforeZero()
    {
        var args = AudioLoader.BuildFfmpegPcmArguments("clip.mp3", startSeconds: 1, durationSeconds: 20);

        Assert.Contains("-ss 0 -i \"clip.mp3\" -ss 1 -t 20", args, StringComparison.Ordinal);
    }
}
