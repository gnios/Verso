namespace Transcriba.Core.Engine;

public static class SilenceSplitter
{
    public const int SampleRate = AudioLoader.SampleRate;
    private const int MinSilenceMs = 500;
    private const double SilenceDb = -40.0;
    private const int FrameMs = 30;
    private const int MinSpeechMs = 100;

    public static List<(double OffsetSec, float[] Samples)> SplitBySilence(float[] audio, int sampleRate = SampleRate)
    {
        if (audio.Length == 0)
            return [];

        var frameSize = Math.Max(1, sampleRate * FrameMs / 1000);
        var hop = Math.Max(1, frameSize / 2);
        var minSilenceFrames = Math.Max(1, MinSilenceMs / FrameMs);
        var minSpeechSamples = sampleRate * MinSpeechMs / 1000;

        var peak = 0f;
        foreach (var sample in audio)
        {
            var abs = Math.Abs(sample);
            if (abs > peak)
                peak = abs;
        }

        var thresholdSq = peak * peak * Math.Pow(10, SilenceDb / 10.0);

        var nFrames = Math.Max(1, (audio.Length - frameSize) / hop + 1);
        var silent = new bool[nFrames];
        for (var i = 0; i < nFrames; i++)
        {
            var start = i * hop;
            var sumSq = 0.0;
            var count = Math.Min(frameSize, audio.Length - start);
            for (var j = 0; j < count; j++)
            {
                var v = audio[start + j];
                sumSq += v * v;
            }

            var meanSq = sumSq / count;
            silent[i] = meanSq < thresholdSq;
        }

        var partes = new List<(double, float[])>();
        var speechStart = 0;
        var silenceRun = 0;

        for (var i = 0; i < nFrames; i++)
        {
            if (silent[i])
            {
                silenceRun++;
                continue;
            }

            if (silenceRun >= minSilenceFrames && i * hop > speechStart)
            {
                var endSample = Math.Max(speechStart, (i - silenceRun) * hop);
                var length = endSample - speechStart;
                if (length >= minSpeechSamples)
                {
                    var chunk = new float[length];
                    Array.Copy(audio, speechStart, chunk, 0, length);
                    partes.Add((speechStart / (double)sampleRate, chunk));
                }
                speechStart = i * hop;
            }

            silenceRun = 0;
        }

        if (audio.Length - speechStart >= minSpeechSamples)
        {
            var chunk = new float[audio.Length - speechStart];
            Array.Copy(audio, speechStart, chunk, 0, chunk.Length);
            partes.Add((speechStart / (double)sampleRate, chunk));
        }

        if (partes.Count == 0)
            partes.Add((0, audio));

        return partes;
    }
}
