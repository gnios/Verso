using Transcriba.Core.Data.Entities;

namespace Transcriba.Core.Engine;

public static class ChunkPlanner
{
    public static (int MaxPartes, int Paralelismo, int ThreadsPorJob) CalculateParallelLimits(ExecutionDevice device)
    {
        var deviceCode = ResolveDeviceCode(device);
        var threads = Environment.ProcessorCount;

        if (deviceCode is "cuda" or "vulkan")
        {
            var paralelismo = Math.Max(1, Math.Min(2, threads / 4));
            var maxPartes = Math.Max(paralelismo, Math.Min(4, (int)Math.Ceiling(threads / 2.0)));
            var threadsPorJob = Math.Max(1, threads / paralelismo);
            return (maxPartes, paralelismo, threadsPorJob);
        }

        var cpuParalelismo = Math.Max(1, threads);
        var cpuThreadsPorJob = Math.Max(1, threads / cpuParalelismo);
        return (cpuParalelismo, cpuParalelismo, cpuThreadsPorJob);
    }

    public static List<(double OffsetSec, float[] Samples)> GroupParts(
        List<(double OffsetSec, float[] Samples)> trechos,
        int maxPartes)
    {
        if (trechos.Count <= maxPartes || maxPartes <= 1)
            return trechos;

        var totalSamples = trechos.Sum(t => (long)t.Samples.Length);
        var targetSamples = totalSamples / (double)maxPartes;

        var agrupadas = new List<(double, float[])>();
        var bufferChunks = new List<float[]>();
        var bufferLen = 0;
        var offsetInicio = trechos[0].OffsetSec;

        foreach (var (offset, samples) in trechos)
        {
            if (bufferLen == 0)
                offsetInicio = offset;

            bufferChunks.Add(samples);
            bufferLen += samples.Length;

            var ultimaParte = agrupadas.Count >= maxPartes - 1;
            var atingiuAlvo = bufferLen >= targetSamples;

            if (!ultimaParte && atingiuAlvo)
            {
                agrupadas.Add((offsetInicio, CopyChunks(bufferChunks, bufferLen)));
                bufferChunks.Clear();
                bufferLen = 0;
            }
        }

        if (bufferLen > 0)
            agrupadas.Add((offsetInicio, CopyChunks(bufferChunks, bufferLen)));

        return agrupadas;
    }

    internal static float[] CopyChunks(List<float[]> chunks, int totalLen)
    {
        var result = new float[totalLen];
        var pos = 0;
        foreach (var chunk in chunks)
        {
            Array.Copy(chunk, 0, result, pos, chunk.Length);
            pos += chunk.Length;
        }

        return result;
    }

    private static string ResolveDeviceCode(ExecutionDevice device) => device switch
    {
        ExecutionDevice.Cpu => "cpu",
        ExecutionDevice.Cuda => "cuda",
        ExecutionDevice.Vulkan => "vulkan",
        ExecutionDevice.Auto => "cuda",
        _ => "cpu",
    };
}
