using Transcriba.Core.Engine;

namespace Transcriba.Tests.Engine;

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
}
