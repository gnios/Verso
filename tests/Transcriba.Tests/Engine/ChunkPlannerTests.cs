using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;

namespace Transcriba.Tests.Engine;

public class ChunkPlannerTests
{
    [Fact]
    public void GroupParts_WhenTrechosLessOrEqualMaxPartes_ReturnsUnchanged()
    {
        var trechos = new List<(double OffsetSec, float[] Samples)>
        {
            (0, [1f, 2f]),
            (1.5, [3f, 4f, 5f]),
        };

        var result = ChunkPlanner.GroupParts(trechos, maxPartes: 3);

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].OffsetSec);
        Assert.Equal(1.5, result[1].OffsetSec);
        Assert.Equal([1f, 2f], result[0].Samples);
        Assert.Equal([3f, 4f, 5f], result[1].Samples);
    }

    [Fact]
    public void GroupParts_WhenTrechosExceedMaxPartes_GroupsRespectingOffsets()
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
        Assert.Equal(0, result[0].OffsetSec);
        Assert.True(result[1].OffsetSec >= 2.0);
        Assert.Equal(3000, result[0].Samples.Length);
        Assert.Equal(3000, result[1].Samples.Length);
    }

    [Fact]
    public void CalculateParallelLimits_ForCuda_ProducesParallelismAtMostTwo()
    {
        var (_, paralelismo, _) = ChunkPlanner.CalculateParallelLimits(ExecutionDevice.Cuda);

        Assert.InRange(paralelismo, 1, 2);
    }

    [Fact]
    public void CalculateParallelLimits_ForVulkan_ProducesParallelismAtMostTwo()
    {
        var (_, paralelismo, _) = ChunkPlanner.CalculateParallelLimits(ExecutionDevice.Vulkan);

        Assert.InRange(paralelismo, 1, 2);
    }

    [Fact]
    public void CalculateParallelLimits_ForCpu_UsesAllProcessorThreads()
    {
        var (maxPartes, paralelismo, threadsPorJob) = ChunkPlanner.CalculateParallelLimits(ExecutionDevice.Cpu);

        Assert.Equal(Environment.ProcessorCount, maxPartes);
        Assert.Equal(Environment.ProcessorCount, paralelismo);
        Assert.Equal(1, threadsPorJob);
    }
}
