namespace Verso.Core.Engine;

/// <summary>
/// Decoder TDT greedy (algoritmo do onnx-asr <c>_AsrWithTransducerDecoding</c>).
/// O estado do decoder só avança quando um token não-blank é emitido.
/// </summary>
public static class ParakeetTdtGreedy
{
    public const int DefaultMaxTokensPerStep = 10;

    public delegate (float[] Logits, int DurationStep, TState State) DecodeFrame<TState>(
        int previousToken,
        TState state,
        float[] encoderFrame);

    public static (List<int> TokenIds, List<int> FrameIndices) Decode<TState>(
        IReadOnlyList<float[]> encoderFrames,
        int blankIndex,
        int vocabSize,
        TState initialState,
        DecodeFrame<TState> decode,
        int maxTokensPerStep = DefaultMaxTokensPerStep)
    {
        var tokens = new List<int>();
        var frames = new List<int>();
        var previousToken = blankIndex;
        var state = initialState;
        var emitted = 0;
        var t = 0;

        while (t < encoderFrames.Count)
        {
            var (logits, durationStep, newState) = decode(previousToken, state, encoderFrames[t]);
            var token = ArgMax(logits, vocabSize);

            if (token != blankIndex)
            {
                state = newState;
                previousToken = token;
                tokens.Add(token);
                frames.Add(t);
                emitted++;
            }

            if (durationStep > 0)
            {
                t += durationStep;
                emitted = 0;
            }
            else if (token == blankIndex || emitted == maxTokensPerStep)
            {
                t += 1;
                emitted = 0;
            }
        }

        return (tokens, frames);
    }

    public static int ArgMax(float[] values, int length)
    {
        var limit = Math.Min(length, values.Length);
        var best = 0;
        var bestValue = float.NegativeInfinity;
        for (var i = 0; i < limit; i++)
        {
            if (values[i] > bestValue)
            {
                bestValue = values[i];
                best = i;
            }
        }

        return best;
    }

    public static int ArgMax(ReadOnlySpan<float> values)
    {
        var best = 0;
        var bestValue = float.NegativeInfinity;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] > bestValue)
            {
                bestValue = values[i];
                best = i;
            }
        }

        return best;
    }
}
