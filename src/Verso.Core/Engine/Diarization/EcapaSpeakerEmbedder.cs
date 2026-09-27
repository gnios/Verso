using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Verso.Core.Engine;

namespace Verso.Core.Engine.Diarization;

public sealed class EcapaSpeakerEmbedder : ISpeakerEmbedder, IDisposable
{
    public const int MinSamples = AudioLoader.SampleRate;

    private readonly EcapaSpeakerModelManager _modelManager;
    private readonly string _modelPath;
    private readonly object _sessionLock = new();
    private InferenceSession? _session;

    public EcapaSpeakerEmbedder(EcapaSpeakerModelManager modelManager)
        : this(modelManager, VersoPaths.ModelsDirectory)
    {
    }

    internal EcapaSpeakerEmbedder(EcapaSpeakerModelManager modelManager, string modelsDirectory)
    {
        _modelManager = modelManager;
        _modelPath = EcapaSpeakerModelManager.GetModelPath(modelsDirectory);
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        await _modelManager.EnsureModelAsync(_modelPath, cancellationToken);
        lock (_sessionLock)
        {
            _session ??= CreateSession(_modelPath);
        }
    }

    public float[] Embed(ReadOnlySpan<float> samples16kHz)
    {
        var session = _session ?? throw new InvalidOperationException(
            "ECAPA ainda não foi preparado. Chame EnsureReadyAsync antes de Embed.");

        var padded = PadToMinimum(samples16kHz);
        lock (_sessionLock)
        {
            using var results = session.Run(BindInputs(session, padded));
            var output = results.First().AsEnumerable<float>().ToArray();
            return SpeakerTurnAssembler.L2Normalize(output);
        }
    }

    public void Dispose() => _session?.Dispose();

    private static InferenceSession CreateSession(string modelPath)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
            InterOpNumThreads = 1,
        };
        options.AppendExecutionProvider_CPU();
        return new InferenceSession(modelPath, options);
    }

    private static float[] PadToMinimum(ReadOnlySpan<float> samples)
    {
        if (samples.Length >= MinSamples)
        {
            return samples.ToArray();
        }

        var padded = new float[MinSamples];
        samples.CopyTo(padded);
        return padded;
    }

    internal static IReadOnlyCollection<NamedOnnxValue> BindInputs(InferenceSession session, float[] samples)
    {
        var bound = new List<NamedOnnxValue>(session.InputMetadata.Count);
        foreach (var (name, meta) in session.InputMetadata)
        {
            if (IsLengthInput(name, meta.ElementType))
            {
                bound.Add(BindLength(name, samples.Length, meta));
            }
            else
            {
                bound.Add(NamedOnnxValue.CreateFromTensor(name, WaveformTensor(samples, meta.Dimensions)));
            }
        }

        return bound;
    }

    internal static DenseTensor<float> WaveformTensor(float[] samples, int[] dimensions)
    {
        var rank = dimensions.Length;
        return rank switch
        {
            0 or 1 => new DenseTensor<float>(samples, new[] { samples.Length }),
            2 => new DenseTensor<float>(samples, new[] { 1, samples.Length }),
            _ => new DenseTensor<float>(samples, new[] { 1, 1, samples.Length }),
        };
    }

    private static bool IsLengthInput(string name, Type elementType) =>
        elementType == typeof(int)
        || elementType == typeof(long)
        || name.Contains("len", StringComparison.OrdinalIgnoreCase);

    private static NamedOnnxValue BindLength(string name, int length, NodeMetadata meta)
    {
        if (meta.ElementType == typeof(int))
        {
            return NamedOnnxValue.CreateFromTensor(
                name,
                new DenseTensor<int>(new[] { length }, new[] { 1 }));
        }

        if (meta.ElementType == typeof(long))
        {
            return NamedOnnxValue.CreateFromTensor(name, LengthTensor(length, meta.Dimensions));
        }

        return NamedOnnxValue.CreateFromTensor(
            name,
            new DenseTensor<float>(new[] { 1f }, new[] { 1 }));
    }

    private static DenseTensor<long> LengthTensor(int length, int[] dimensions)
    {
        if (dimensions.Length <= 1)
        {
            return new DenseTensor<long>(new[] { (long)length }, new[] { 1 });
        }

        return new DenseTensor<long>(new[] { (long)length }, new[] { 1, 1 });
    }
}
