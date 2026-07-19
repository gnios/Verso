using Verso.Core.Data.Entities;
using Verso.Core.Engine;

namespace Verso.Tests.Engine;

public class ModelRecommenderTests
{
    private const long GB = 1024L * 1024L * 1024L;

    [Theory]
    [InlineData(ExecutionDevice.Cpu, 4L * GB, ModelQuality.Tiny)]
    [InlineData(ExecutionDevice.Cpu, 8L * GB, ModelQuality.Base)]
    [InlineData(ExecutionDevice.Cpu, 16L * GB, ModelQuality.Standard)]
    [InlineData(ExecutionDevice.Cpu, 32L * GB, ModelQuality.Medium)]
    [InlineData(ExecutionDevice.Cuda, 8L * GB, ModelQuality.LargeV3Turbo)]
    [InlineData(ExecutionDevice.Cuda, 16L * GB, ModelQuality.LargeV3Turbo)]
    [InlineData(ExecutionDevice.Cuda, 40L * GB, ModelQuality.High)]
    [InlineData(ExecutionDevice.Vulkan, 16L * GB, ModelQuality.LargeV3Turbo)]
    [InlineData(ExecutionDevice.Auto, 16L * GB, ModelQuality.Standard)]
    public void Recommend_ReturnsExpectedQualityWithNonEmptyReason(ExecutionDevice device, long ramBytes, ModelQuality expected)
    {
        var recommendation = ModelRecommender.Recommend(device, ramBytes);

        Assert.Equal(expected, recommendation.Quality);
        Assert.False(string.IsNullOrEmpty(recommendation.Reason));
    }

    [Fact]
    public void Recommend_ZeroRam_FallsBackToSixteenGbOnCpu()
    {
        var recommendation = ModelRecommender.Recommend(ExecutionDevice.Cpu, 0L);

        Assert.Equal(ModelQuality.Standard, recommendation.Quality);
        Assert.False(string.IsNullOrEmpty(recommendation.Reason));
    }

    [Fact]
    public void Recommend_ReasonIsNeverEmptyAcrossTestedCombinations()
    {
        var devices = new[]
        {
            ExecutionDevice.Auto,
            ExecutionDevice.Cpu,
            ExecutionDevice.Cuda,
            ExecutionDevice.Vulkan,
            ExecutionDevice.CoreMl,
        };
        var ramSizes = new[] { 0L, 4L * GB, 8L * GB, 16L * GB, 32L * GB, 40L * GB };

        foreach (var device in devices)
        {
            foreach (var ram in ramSizes)
            {
                var recommendation = ModelRecommender.Recommend(device, ram);
                Assert.False(string.IsNullOrEmpty(recommendation.Reason),
                    $"Reason empty for device={device}, ram={ram}");
            }
        }
    }
}