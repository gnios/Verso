using Verso.Core.Data.Entities;
using Verso.Core.Engine;

namespace Verso.Tests.Engine;

public class ChunkPlannerTests
{
    [Fact]
    public void GroupParts_WhenTrechosLessOrEqualMaxPartes_ReturnsOnePartPerTrechoWithSingleSpan()
    {
        var trechos = new List<(double OffsetSec, float[] Samples)>
        {
            (0, [1f, 2f]),
            (1.5, [3f, 4f, 5f]),
        };

        var result = ChunkPlanner.GroupParts(trechos, maxPartes: 3);

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].Chunks[0].OriginalOffsetSec);
        Assert.Equal(1.5, result[1].Chunks[0].OriginalOffsetSec);
        Assert.Equal([1f, 2f], result[0].Samples);
        Assert.Equal([3f, 4f, 5f], result[1].Samples);
        // Cada parte tem um único ChunkSpan começando em 0 dentro da parte.
        Assert.Single(result[0].Chunks);
        Assert.Equal(0, result[0].Chunks[0].StartInPartSec);
    }

    [Fact]
    public void GroupParts_WhenTrechosExceedMaxPartes_GroupsRespectingOffsetsAndRecordsSpans()
    {
        var trechos = new List<(double OffsetSec, float[] Samples)>
        {
            (0, new float[1000]),
            (1.0, new float[1000]),
            (2.0, new float[1000]),
            (3.0, new float[1000]),
            (4.0, new float[1000]),
            (5.0, new float[1000]),
        };

        var result = ChunkPlanner.GroupParts(trechos, maxPartes: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].Chunks[0].OriginalOffsetSec);
        Assert.True(result[1].Chunks[0].OriginalOffsetSec >= 2.0);
        Assert.Equal(3000, result[0].Samples.Length);
        Assert.Equal(3000, result[1].Samples.Length);
        // A primeira parte agrupa 3 trechos → 3 ChunkSpans, contíguos dentro da parte.
        Assert.Equal(3, result[0].Chunks.Count);
        Assert.Equal(0, result[0].Chunks[0].StartInPartSec);
        Assert.Equal(1000 / (double)AudioLoader.SampleRate, result[0].Chunks[0].DurationSec);
        Assert.Equal(1000 / (double)AudioLoader.SampleRate, result[0].Chunks[1].StartInPartSec);
    }

    [Fact]
    public void MapToRealTime_WithSilenceGaps_MapsToOriginalOffsetNotPartOffset()
    {
        // Trechos com silêncio entre eles (offsets 0, 5, 10, 15; cada trecho 1s de áudio).
        // Sem o mapeamento, um segmento do whisper no 2º trecho da parte (tempo 1.5s
        // dentro da parte) mapearia para partOffset(0) + 1.5 = 1.5s — errado, deveria ser
        // 5.0 + (1.5 - 1.0) = 5.5s (offset original do 2º trecho + tempo dentro dele).
        var trechos = new List<(double OffsetSec, float[] Samples)>
        {
            (0, new float[AudioLoader.SampleRate]),     // 1s @ 16kHz
            (5, new float[AudioLoader.SampleRate]),
            (10, new float[AudioLoader.SampleRate]),
            (15, new float[AudioLoader.SampleRate]),
        };

        var result = ChunkPlanner.GroupParts(trechos, maxPartes: 2);
        var part0 = result[0]; // trechos 0 e 5 → spans [ChunkSpan(0,0,1), ChunkSpan(5,1,1)]

        // 0.5s cai no 1º trecho (0..1 dentro da parte) → 0 + 0.5 = 0.5
        Assert.Equal(0.5, ChunkPlanner.MapToRealTime(0.5, part0.Chunks), precision: 6);
        // 1.5s cai no 2º trecho (1..2 dentro da parte, offset original 5) → 5 + 0.5 = 5.5
        Assert.Equal(5.5, ChunkPlanner.MapToRealTime(1.5, part0.Chunks), precision: 6);
    }

    [Fact]
    public void MapToRealTime_BeyondLastChunk_UsesLastChunkOffset()
    {
        var chunks = new[]
        {
            new ChunkSpan(0, 0, 1),
            new ChunkSpan(5, 1, 1),
        };

        // 2.5s está além do último trecho (1..2) → usa o último: 5 + (2.5 - 1) = 6.5
        Assert.Equal(6.5, ChunkPlanner.MapToRealTime(2.5, chunks), precision: 6);
    }

    // CPU: paralelismo é 1 (whisper.cpp já satura todos os núcleos).
    // GPU: paralelismo é 2 (contextos independentes paralelizáveis).
    [Fact]
    public void CalculateParallelLimits_ForCpu_IsSequential()
    {
        var (maxPartes, paralelismo, threadsPorJob) = ChunkPlanner.CalculateParallelLimits(ExecutionDevice.Cpu);

        Assert.Equal(1, paralelismo);
        Assert.Equal(Environment.ProcessorCount, threadsPorJob);
        Assert.InRange(maxPartes, 4, 8);
    }

    [Theory]
    [InlineData(ExecutionDevice.Cuda)]
    [InlineData(ExecutionDevice.Vulkan)]
    [InlineData(ExecutionDevice.Auto)]
    public void CalculateParallelLimits_ForGpu_RunsTwoParallelInstances(ExecutionDevice device)
    {
        var (maxPartes, paralelismo, threadsPorJob) = ChunkPlanner.CalculateParallelLimits(device);

        // Paralelismo = min(2, maxPartes). Como maxPartes ≥ 4, esperamos 2.
        Assert.Equal(2, paralelismo);
        Assert.Equal(Environment.ProcessorCount, threadsPorJob);
        Assert.InRange(maxPartes, 4, 8);
    }
}