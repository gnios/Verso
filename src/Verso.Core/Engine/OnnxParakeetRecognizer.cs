using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Verso.Core.Engine;

public interface IParakeetRecognizer
{
    IReadOnlyList<TranscriptionSegmentResult> Recognize(
        float[] samples16kHz,
        CancellationToken cancellationToken = default,
        IProgress<EngineProgress>? progress = null);
}

public interface IParakeetRecognizerFactory
{
    IParakeetRecognizer Create(string modelDirectory, int threads);
}

public sealed class OnnxParakeetRecognizerFactory : IParakeetRecognizerFactory
{
    public IParakeetRecognizer Create(string modelDirectory, int threads) =>
        new OnnxParakeetRecognizer(modelDirectory, threads);
}

public sealed class OnnxParakeetRecognizer : IParakeetRecognizer, IDisposable
{
    public const double SecondsPerFrame = 0.08;

    private readonly InferenceSession _preprocessor;
    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoderJoint;
    private readonly ParakeetVocab _vocab;
    private readonly int[] _state1Shape;
    private readonly int[] _state2Shape;
    private readonly string _waveformsName;
    private readonly string _waveformsLensName;
    private readonly string _audioSignalName;
    private readonly string _audioLengthName;
    private readonly string _encoderOutName;
    private readonly string _targetsName;
    private readonly string _targetLengthName;
    private readonly string _state1InName;
    private readonly string _state2InName;
    private readonly bool _targetsAreInt64;
    private readonly bool _lengthIsInt64;

    public OnnxParakeetRecognizer(string modelDirectory, int threads)
    {
        var intraOp = Math.Max(1, threads);
        _preprocessor = CreateSession(Path.Combine(modelDirectory, ParakeetModelManager.PreprocessorFileName), intraOp);
        _encoder = CreateSession(Path.Combine(modelDirectory, ParakeetModelManager.EncoderFileName), intraOp);
        _decoderJoint = CreateSession(Path.Combine(modelDirectory, ParakeetModelManager.DecoderJointFileName), intraOp);
        _vocab = ParakeetVocab.Load(Path.Combine(modelDirectory, ParakeetModelManager.VocabFileName));

        _waveformsName = FindName(_preprocessor.InputMetadata.Keys, "waveforms", 0);
        _waveformsLensName = FindName(_preprocessor.InputMetadata.Keys, "lens", 1);
        _audioSignalName = FindName(_encoder.InputMetadata.Keys, "audio", 0);
        _audioLengthName = FindName(_encoder.InputMetadata.Keys, "length", 1);

        _encoderOutName = FindName(_decoderJoint.InputMetadata.Keys, "encoder", 0);
        _targetsName = FindName(_decoderJoint.InputMetadata.Keys, "targets", 1);
        _targetLengthName = FindName(_decoderJoint.InputMetadata.Keys, "length", 2);
        _state1InName = FindName(_decoderJoint.InputMetadata.Keys, "states_1", 3);
        _state2InName = FindName(_decoderJoint.InputMetadata.Keys, "states_2", 4);

        _targetsAreInt64 = _decoderJoint.InputMetadata[_targetsName].ElementType == typeof(long);
        _lengthIsInt64 = _decoderJoint.InputMetadata[_targetLengthName].ElementType == typeof(long);
        _state1Shape = NormalizeShape(_decoderJoint.InputMetadata[_state1InName].Dimensions);
        _state2Shape = NormalizeShape(_decoderJoint.InputMetadata[_state2InName].Dimensions);
    }

    public IReadOnlyList<TranscriptionSegmentResult> Recognize(
        float[] samples16kHz,
        CancellationToken cancellationToken = default,
        IProgress<EngineProgress>? progress = null)
    {
        if (samples16kHz.Length == 0)
        {
            return [];
        }

        var chunks = ParakeetAudioChunker.Split(samples16kHz);
        progress?.Report(new EngineProgress("transcribing", 0, chunks.Count));
        var windows = new List<ParakeetAudioChunker.WindowTranscript>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = chunks[i];
            var (timestamps, tokens) = RecognizeWindowTokens(chunk.Samples, cancellationToken);
            windows.Add(new ParakeetAudioChunker.WindowTranscript(
                chunk.OffsetSamples / (double)AudioLoader.SampleRate,
                chunk.Samples.Length / (double)AudioLoader.SampleRate,
                timestamps,
                tokens));
            progress?.Report(new EngineProgress("transcribing", i + 1, chunks.Count));
        }

        return ParakeetAudioChunker.StitchWindowTokens(windows);
    }

    private (IReadOnlyList<double> Timestamps, IReadOnlyList<string> Tokens) RecognizeWindowTokens(
        float[] samples16kHz,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (features, featureLens) = RunPreprocessor(samples16kHz);
        cancellationToken.ThrowIfCancellationRequested();
        var frames = RunEncoder(features, featureLens);
        if (frames.Count == 0)
        {
            return ([], []);
        }

        var state = (Zeros(_state1Shape), Zeros(_state2Shape));
        var (tokenIds, frameIndices) = ParakeetTdtGreedy.Decode(
            frames,
            _vocab.BlankIndex,
            _vocab.Size,
            state,
            (prevToken, current, frame) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return RunDecoderJoint(prevToken, current, frame);
            });

        var tokens = tokenIds.Select(id => _vocab[id]).ToList();
        var timestamps = frameIndices.Select(f => f * SecondsPerFrame).ToList();
        return (timestamps, tokens);
    }

    public void Dispose()
    {
        _preprocessor.Dispose();
        _encoder.Dispose();
        _decoderJoint.Dispose();
    }

    private static InferenceSession CreateSession(string modelPath, int threads)
    {
        try
        {
            return new InferenceSession(modelPath, CreateOptions(threads, modelPath));
        }
        catch (OnnxRuntimeException)
        {
            return new InferenceSession(modelPath, CreateOptions(threads, optimizedModelPath: null));
        }
    }

    private static SessionOptions CreateOptions(int threads, string? optimizedModelPath)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
            IntraOpNumThreads = threads,
            InterOpNumThreads = 1,
        };
        options.AppendExecutionProvider_CPU();

        if (!string.IsNullOrWhiteSpace(optimizedModelPath))
        {
            var cacheDir = Path.Combine(Path.GetDirectoryName(optimizedModelPath)!, ".ort");
            Directory.CreateDirectory(cacheDir);
            options.OptimizedModelFilePath = Path.Combine(cacheDir, Path.GetFileName(optimizedModelPath) + ".opt");
        }

        return options;
    }

    private (DenseTensor<float> Features, long FeatureLength) RunPreprocessor(float[] samples)
    {
        var waveforms = new DenseTensor<float>(samples, [1, samples.Length]);
        using var results = _preprocessor.Run(
        [
            NamedOnnxValue.CreateFromTensor(_waveformsName, waveforms),
            CreateLengthValue(_preprocessor.InputMetadata, _waveformsLensName, samples.Length),
        ]);
        var list = results.ToList();
        var features = CopyTensor(list[0].AsTensor<float>());
        var featureLength = list.Count > 1
            ? list[1].AsTensor<long>().ToArray()[0]
            : features.Dimensions[^1];
        return (features, featureLength);
    }

    private List<float[]> RunEncoder(DenseTensor<float> features, long featureLength)
    {
        using var results = _encoder.Run(
        [
            NamedOnnxValue.CreateFromTensor(_audioSignalName, features),
            CreateLengthValue(_encoder.InputMetadata, _audioLengthName, featureLength),
        ]);
        var list = results.ToList();
        var encoderOut = list[0].AsTensor<float>();
        var encodedLength = list.Count > 1
            ? (int)list[1].AsTensor<long>().ToArray()[0]
            : encoderOut.Dimensions[^1];

        var expectedChannels = _decoderJoint.InputMetadata[_encoderOutName].Dimensions[1];
        if (expectedChannels <= 0)
        {
            expectedChannels = ParakeetEncoderFrames.ParakeetEncoderChannels;
        }

        return ParakeetEncoderFrames.Extract(encoderOut, encodedLength, expectedChannels);
    }

    private (float[] Logits, int DurationStep, (float[] S1, float[] S2) State) RunDecoderJoint(
        int previousToken,
        (float[] S1, float[] S2) state,
        float[] encoderFrame)
    {
        // decoder_joint espera encoder_outputs [B, C, T] = [1, 1024, 1] (um frame).
        var encoderTensor = new DenseTensor<float>(encoderFrame, [1, encoderFrame.Length, 1]);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_encoderOutName, encoderTensor),
            NamedOnnxValue.CreateFromTensor(_state1InName, new DenseTensor<float>(state.S1, _state1Shape)),
            NamedOnnxValue.CreateFromTensor(_state2InName, new DenseTensor<float>(state.S2, _state2Shape)),
        };

        if (_targetsAreInt64)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(_targetsName, new DenseTensor<long>(new[] { (long)previousToken }, [1, 1])));
        }
        else
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(_targetsName, new DenseTensor<int>(new[] { previousToken }, [1, 1])));
        }

        if (_lengthIsInt64)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(_targetLengthName, new DenseTensor<long>(new[] { 1L }, [1])));
        }
        else
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(_targetLengthName, new DenseTensor<int>(new[] { 1 }, [1])));
        }

        using var results = _decoderJoint.Run(inputs);
        var list = results.ToList();
        var output = list[0].AsTensor<float>().ToArray();
        var vocabSize = _vocab.Size;
        var tokenLogits = output.Length > vocabSize ? output[..vocabSize] : output;
        var durationLogits = output.Length > vocabSize ? output[vocabSize..] : [1f];
        var durationStep = ParakeetTdtGreedy.ArgMax(durationLogits.AsSpan());

        var out1 = FindOutput(list, "states_1", 1).AsTensor<float>().ToArray();
        var out2 = FindOutput(list, "states_2", 2).AsTensor<float>().ToArray();
        return (tokenLogits, durationStep, (out1, out2));
    }

    private static DisposableNamedOnnxValue FindOutput(
        IReadOnlyList<DisposableNamedOnnxValue> outputs,
        string hint,
        int fallbackIndex)
    {
        return outputs.FirstOrDefault(o => o.Name.Contains(hint, StringComparison.OrdinalIgnoreCase))
               ?? outputs[Math.Min(fallbackIndex, outputs.Count - 1)];
    }

    private static DenseTensor<float> CopyTensor(Tensor<float> source)
    {
        var dims = source.Dimensions.ToArray();
        return new DenseTensor<float>(source.ToArray(), dims);
    }

    private static NamedOnnxValue CreateLengthValue(
        IReadOnlyDictionary<string, NodeMetadata> metadata,
        string name,
        long value)
    {
        if (metadata.TryGetValue(name, out var meta) && meta.ElementType == typeof(int))
        {
            return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<int>(new[] { (int)value }, new[] { 1 }));
        }

        return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(new[] { value }, new[] { 1 }));
    }

    private static string FindName(IEnumerable<string> names, string hint, int fallbackIndex)
    {
        var list = names.ToList();
        return list.FirstOrDefault(n => n.Contains(hint, StringComparison.OrdinalIgnoreCase))
               ?? list[fallbackIndex];
    }

    private static int[] NormalizeShape(int[] dimensions) =>
        dimensions.Select(d => d <= 0 ? 1 : d).ToArray();

    private static float[] Zeros(int[] shape)
    {
        var len = shape.Aggregate(1, (a, b) => a * b);
        return new float[len];
    }
}
