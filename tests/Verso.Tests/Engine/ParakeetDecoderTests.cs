using Microsoft.ML.OnnxRuntime.Tensors;
using Verso.Core.Engine;

namespace Verso.Tests.Engine;

public class ParakeetVocabTests
{
    [Fact]
    public void Load_ParsesTokenIdsAndTreatsUnderscoreAsSpace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"verso-vocab-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "a 0\n\u2581 1\nb 2\n<blk> 3\n");
        try
        {
            var vocab = ParakeetVocab.Load(path);
            Assert.Equal(4, vocab.Size);
            Assert.Equal(3, vocab.BlankIndex);
            Assert.Equal("a", vocab[0]);
            Assert.Equal(" ", vocab[1]);
            Assert.Equal("a b", vocab.Join([0, 1, 2]));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ParakeetSegmentBuilderTests
{
    [Fact]
    public void Build_EmptyTokens_ReturnsEmptyList()
    {
        Assert.Empty(ParakeetSegmentBuilder.Build("", [], []));
    }

    [Fact]
    public void Build_SplitsWhenPauseIsAtLeastDefault()
    {
        var tokens = new[] { "olá ", "mundo", " depois" };
        var timestamps = new[] { 0.0, 0.16, 1.0 };
        var segments = ParakeetSegmentBuilder.Build("olá mundo depois", timestamps, tokens);

        Assert.Equal(2, segments.Count);
        Assert.Equal("olá mundo", segments[0].Text);
        Assert.Equal(0, segments[0].StartSeconds, precision: 2);
        Assert.Equal("depois", segments[1].Text);
        Assert.Equal(1.0, segments[1].StartSeconds, precision: 2);
        Assert.True(segments[1].EndSeconds > segments[1].StartSeconds);
    }
}

public class ParakeetTdtGreedyTests
{
    [Fact]
    public void Decode_EmitsNonBlankTokensAndAdvancesDuration()
    {
        var frames = new[]
        {
            new float[] { 0 },
            new float[] { 1 },
            new float[] { 2 },
        };

        var (tokens, tokenFrames) = ParakeetTdtGreedy.Decode(
            frames,
            blankIndex: 0,
            vocabSize: 3,
            initialState: 0,
            decode: (prev, state, frame) =>
            {
                var t = (int)frame[0];
                if (t == 0)
                {
                    return ([10f, 1f, 0f], 1, state);
                }

                if (t == 1)
                {
                    return ([0f, 5f, 1f], 1, state + 1);
                }

                return ([8f, 0f, 0f], 1, state);
            });

        Assert.Equal([1], tokens);
        Assert.Equal([1], tokenFrames);
    }

    [Fact]
    public void ArgMax_ReturnsHighestIndexInRange()
    {
        Assert.Equal(2, ParakeetTdtGreedy.ArgMax([0.1f, 0.2f, 0.9f, 0.4f], 3));
    }
}

public class ParakeetEncoderFramesTests
{
    [Fact]
    public void Extract_ChannelsFirstShortAudio_ReturnsOneFramePerTimeStep()
    {
        var tensor = ChannelsFirstTensor(channels: 1024, time: 5);
        var frames = ParakeetEncoderFrames.Extract(tensor, encodedLength: 5);

        Assert.Equal(5, frames.Count);
        Assert.Equal(1024, frames[0].Length);
        Assert.Equal(3f, frames[3][0]);
        Assert.Equal(7f, frames[3][1]);
    }

    [Fact]
    public void Extract_ChannelsFirstWhenTimeExceedsChannels_DoesNotSwapAxes()
    {
        // O bug: T=2000 > C=1024 fazia a heurística dims[1] >= dims[2] falhar
        // e mandar 2000 "canais" para o decoder_joint (que espera 1024).
        var tensor = ChannelsFirstTensor(channels: 1024, time: 2000);
        var frames = ParakeetEncoderFrames.Extract(tensor, encodedLength: 2000);

        Assert.Equal(2000, frames.Count);
        Assert.All(frames, frame => Assert.Equal(1024, frame.Length));
        Assert.Equal(1999f, frames[1999][0]);
        Assert.Equal(7f, frames[3][1]);
    }

    private static DenseTensor<float> ChannelsFirstTensor(int channels, int time)
    {
        var data = new float[1 * channels * time];
        var tensor = new DenseTensor<float>(data, [1, channels, time]);
        for (var t = 0; t < time; t++)
        {
            tensor[0, 0, t] = t;
            tensor[0, 1, t] = 4 + t;
        }

        return tensor;
    }
}

public class ParakeetAudioChunkerTests
{
    [Fact]
    public void Split_ShortAudio_ReturnsSingleChunk()
    {
        var samples = new float[AudioLoader.SampleRate * 5];
        var chunks = ParakeetAudioChunker.Split(samples);

        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].OffsetSamples);
        Assert.Same(samples, chunks[0].Samples);
    }

    [Fact]
    public void Split_LongAudio_UsesOverlapBetweenWindows()
    {
        var seconds = 50;
        var samples = new float[AudioLoader.SampleRate * seconds];
        var chunks = ParakeetAudioChunker.Split(samples);

        Assert.True(chunks.Count >= 3);
        Assert.Equal(0, chunks[0].OffsetSamples);
        Assert.Equal(
            (ParakeetAudioChunker.WindowSeconds - ParakeetAudioChunker.OverlapSeconds) * AudioLoader.SampleRate,
            chunks[1].OffsetSamples);
        Assert.Equal(ParakeetAudioChunker.WindowSamples(), chunks[0].Samples.Length);
        Assert.Equal(samples.Length, chunks[^1].OffsetSamples + chunks[^1].Samples.Length);
        Assert.Equal(chunks.Count, ParakeetAudioChunker.CountWindows(samples.Length));
    }

    [Fact]
    public void CountWindowsFromDuration_MatchesWindowsFromDuration()
    {
        Assert.Equal(1, ParakeetAudioChunker.CountWindowsFromDuration(5));
        Assert.Equal(1, ParakeetAudioChunker.CountWindowsFromDuration(20));
        Assert.Equal(2, ParakeetAudioChunker.CountWindowsFromDuration(20.1));
        Assert.Equal(3, ParakeetAudioChunker.CountWindowsFromDuration(50));
        Assert.Equal(3, ParakeetAudioChunker.WindowsFromDuration(50).Count);
        Assert.Equal(20, ParakeetAudioChunker.WindowsFromDuration(50)[0].LengthSeconds, precision: 5);
        Assert.Equal(14, ParakeetAudioChunker.WindowsFromDuration(50)[2].LengthSeconds, precision: 5);
    }

    [Fact]
    public void ShiftAndTrimOverlap_DropsFirstHalfOfOverlapOnLaterChunks()
    {
        var segments = new[]
        {
            new TranscriptionSegmentResult(0.2, 0.8, "drop"),
            new TranscriptionSegmentResult(1.5, 2.0, "keep"),
        };

        var shifted = ParakeetAudioChunker.ShiftAndTrimOverlap(
            segments,
            chunkStartSeconds: 18,
            isFirstChunk: false,
            overlapSeconds: 2);

        Assert.Single(shifted);
        Assert.Equal("keep", shifted[0].Text);
        Assert.Equal(19.5, shifted[0].StartSeconds, precision: 2);
        Assert.Equal(20.0, shifted[0].EndSeconds, precision: 2);
    }
}

public class WordErrorRateTests
{
    [Fact]
    public void Compute_IdenticalTranscripts_IsZero()
    {
        Assert.Equal(0, WordErrorRate.Compute("olá mundo", "olá mundo"));
    }

    [Fact]
    public void Compute_OneSubstitutionInTwoWords_IsHalf()
    {
        Assert.Equal(0.5, WordErrorRate.Compute("olá mundo", "olá terra"), precision: 4);
    }

    [Fact]
    public void Compute_IgnoresPunctuationAndCasing()
    {
        Assert.Equal(0, WordErrorRate.Compute(
            "olá este é um teste",
            "Olá, este é um teste."));
    }
}
