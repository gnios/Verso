using Microsoft.ML.OnnxRuntime.Tensors;

namespace Verso.Core.Engine;

/// <summary>
/// Extrai frames do encoder NeMo/onnx-asr. O export ONNX do Parakeet TDT é sempre
/// <c>[B, C, T]</c> com C=1024; a heurística antiga <c>dims[1] &gt;= dims[2]</c> invertia
/// os eixos quando T &gt; 1024 (~82 s de áudio) e o decoder_joint recebia
/// <c>encoder_outputs</c> com 4689 canais em vez de 1024.
/// </summary>
public static class ParakeetEncoderFrames
{
    public const int ParakeetEncoderChannels = 1024;

    public static List<float[]> Extract(Tensor<float> encoderOut, int encodedLength, int expectedChannels = ParakeetEncoderChannels)
    {
        var dims = encoderOut.Dimensions.ToArray();
        if (dims.Length != 3)
        {
            throw new InvalidOperationException(
                $"Saída do encoder Parakeet inesperada: rank {dims.Length}, dims=[{string.Join(",", dims)}].");
        }

        if (expectedChannels <= 0)
        {
            expectedChannels = ParakeetEncoderChannels;
        }

        if (dims[1] == expectedChannels)
        {
            var time = ClampTime(encodedLength, dims[2]);
            return ExtractChannelsFirst(encoderOut, expectedChannels, time);
        }

        if (dims[2] == expectedChannels)
        {
            var time = ClampTime(encodedLength, dims[1]);
            return ExtractTimeFirst(encoderOut, expectedChannels, time);
        }

        throw new InvalidOperationException(
            $"Saída do encoder [{dims[1]}, {dims[2]}] não contém {expectedChannels} canais.");
    }

    private static int ClampTime(int encodedLength, int timeDim)
    {
        if (encodedLength <= 0)
        {
            return timeDim;
        }

        return Math.Min(encodedLength, timeDim);
    }

    private static List<float[]> ExtractChannelsFirst(Tensor<float> encoderOut, int channels, int time)
    {
        var frames = new List<float[]>(time);
        for (var t = 0; t < time; t++)
        {
            var frame = new float[channels];
            for (var c = 0; c < channels; c++)
            {
                frame[c] = encoderOut[0, c, t];
            }

            frames.Add(frame);
        }

        return frames;
    }

    private static List<float[]> ExtractTimeFirst(Tensor<float> encoderOut, int channels, int time)
    {
        var frames = new List<float[]>(time);
        for (var t = 0; t < time; t++)
        {
            var frame = new float[channels];
            for (var c = 0; c < channels; c++)
            {
                frame[c] = encoderOut[0, t, c];
            }

            frames.Add(frame);
        }

        return frames;
    }
}
