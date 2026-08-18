namespace Verso.Core.Engine.Diarization;

public readonly record struct AcousticFrame(
    double TimeSeconds,
    float LogEnergy,
    float PitchHz,
    float[] Mfcc);

public static class AcousticFeatureExtractor
{
    public const int SampleRate = AudioLoader.SampleRate;
    public const int WindowMs = 25;
    public const int HopMs = 10;
    public const int MfccCount = 12;
    public const int MelFilters = 26;
    public const int FftSize = 512;
    public const float MinPitchHz = 80f;
    public const float MaxPitchHz = 400f;

    private const float EnergyEpsilon = 1e-10f;
    private const float VoicingThreshold = 0.3f;

    public static AcousticFrame[] Extract(float[] samples, int sampleRate = SampleRate)
    {
        if (samples.Length == 0)
        {
            return [];
        }

        var windowSize = Math.Max(1, sampleRate * WindowMs / 1000);
        var hop = Math.Max(1, sampleRate * HopMs / 1000);
        var nFrames = samples.Length <= windowSize
            ? 1
            : 1 + (samples.Length - windowSize) / hop;

        var hamming = BuildHamming(windowSize);
        var melFilters = BuildMelFilterbank(sampleRate);
        var dct = BuildDctMatrix();

        var frames = new AcousticFrame[nFrames];
        var re = new double[FftSize];
        var im = new double[FftSize];
        var power = new double[FftSize / 2 + 1];
        var logMel = new double[MelFilters];

        for (var f = 0; f < nFrames; f++)
        {
            var start = nFrames == 1
                ? 0
                : f * hop;
            if (start + windowSize > samples.Length)
            {
                start = Math.Max(0, samples.Length - windowSize);
            }

            var energy = 0.0;
            Array.Clear(re);
            Array.Clear(im);
            var count = Math.Min(windowSize, samples.Length - start);
            for (var i = 0; i < count; i++)
            {
                var v = samples[start + i] * hamming[i];
                re[i] = v;
                energy += v * v;
            }

            Fft(re, im);
            var nBins = FftSize / 2 + 1;
            for (var k = 0; k < nBins; k++)
            {
                power[k] = re[k] * re[k] + im[k] * im[k];
            }

            for (var m = 0; m < MelFilters; m++)
            {
                var sum = 0.0;
                var filter = melFilters[m];
                for (var k = 0; k < nBins; k++)
                {
                    sum += power[k] * filter[k];
                }

                logMel[m] = Math.Log(sum + EnergyEpsilon);
            }

            var mfcc = new float[MfccCount];
            for (var c = 0; c < MfccCount; c++)
            {
                var sum = 0.0;
                for (var m = 0; m < MelFilters; m++)
                {
                    sum += dct[c][m] * logMel[m];
                }

                mfcc[c] = (float)sum;
            }

            var window = samples.AsSpan(start, count);
            frames[f] = new AcousticFrame(
                start / (double)sampleRate,
                (float)Math.Log(energy + EnergyEpsilon),
                EstimatePitch(window, sampleRate),
                mfcc);
        }

        return frames;
    }

    public static float[] MeanEmbedding(IReadOnlyList<AcousticFrame> frames)
    {
        var vector = new float[MfccCount + 1];
        if (frames.Count == 0)
        {
            return vector;
        }

        var pitchSum = 0.0;
        var pitchCount = 0;
        foreach (var frame in frames)
        {
            for (var i = 0; i < MfccCount; i++)
            {
                vector[i] += frame.Mfcc[i];
            }

            if (frame.PitchHz > 0)
            {
                pitchSum += frame.PitchHz;
                pitchCount++;
            }
        }

        var inv = 1f / frames.Count;
        for (var i = 0; i < MfccCount; i++)
        {
            vector[i] *= inv;
        }

        vector[MfccCount] = pitchCount == 0 ? 0 : (float)(pitchSum / pitchCount);
        return vector;
    }

    internal static float EstimatePitch(ReadOnlySpan<float> window, int sampleRate)
    {
        var minLag = Math.Max(1, (int)(sampleRate / MaxPitchHz));
        var maxLag = (int)(sampleRate / MinPitchHz);
        maxLag = Math.Min(maxLag, window.Length / 2);
        if (maxLag <= minLag || window.Length < maxLag + 8)
        {
            return 0;
        }

        var energy = 0.0;
        for (var i = 0; i < window.Length; i++)
        {
            energy += window[i] * window[i];
        }

        if (energy < 1e-8)
        {
            return 0;
        }

        var bestLag = minLag;
        var best = double.MinValue;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            var sum = 0.0;
            var n = window.Length - lag;
            for (var i = 0; i < n; i++)
            {
                sum += window[i] * window[i + lag];
            }

            var corr = sum / energy;
            if (corr > best)
            {
                best = corr;
                bestLag = lag;
            }
        }

        if (best < VoicingThreshold)
        {
            return 0;
        }

        return sampleRate / (float)bestLag;
    }

    private static float[] BuildHamming(int size)
    {
        var window = new float[size];
        if (size == 1)
        {
            window[0] = 1;
            return window;
        }

        for (var i = 0; i < size; i++)
        {
            window[i] = 0.54f - 0.46f * MathF.Cos(2 * MathF.PI * i / (size - 1));
        }

        return window;
    }

    private static double[][] BuildMelFilterbank(int sampleRate)
    {
        var nBins = FftSize / 2 + 1;
        var filters = new double[MelFilters][];
        var lowMel = HzToMel(0);
        var highMel = HzToMel(sampleRate / 2.0);
        var points = new double[MelFilters + 2];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = MelToHz(lowMel + (highMel - lowMel) * i / (MelFilters + 1));
        }

        var bin = new int[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            bin[i] = Math.Clamp((int)Math.Floor((FftSize + 1) * points[i] / sampleRate), 0, nBins - 1);
        }

        for (var m = 0; m < MelFilters; m++)
        {
            var filter = new double[nBins];
            var left = bin[m];
            var center = bin[m + 1];
            var right = bin[m + 2];
            if (center == left)
            {
                center = Math.Min(left + 1, nBins - 1);
            }

            if (right == center)
            {
                right = Math.Min(center + 1, nBins - 1);
            }

            for (var k = left; k < center; k++)
            {
                filter[k] = (k - left) / (double)(center - left);
            }

            filter[center] = 1;
            for (var k = center + 1; k <= right; k++)
            {
                filter[k] = (right - k) / (double)(right - center);
            }

            filters[m] = filter;
        }

        return filters;
    }

    private static double[][] BuildDctMatrix()
    {
        var matrix = new double[MfccCount][];
        for (var c = 0; c < MfccCount; c++)
        {
            var row = new double[MelFilters];
            var k = c + 1;
            for (var m = 0; m < MelFilters; m++)
            {
                row[m] = Math.Cos(Math.PI * k * (m + 0.5) / MelFilters);
            }

            matrix[c] = row;
        }

        return matrix;
    }

    private static double HzToMel(double hz) => 2595.0 * Math.Log10(1.0 + hz / 700.0);

    private static double MelToHz(double mel) => 700.0 * (Math.Pow(10.0, mel / 2595.0) - 1.0);

    internal static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        var bits = (int)Math.Log2(n);
        for (var i = 0; i < n; i++)
        {
            var j = BitReverse(i, bits);
            if (j > i)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var size = 2; size <= n; size <<= 1)
        {
            var half = size / 2;
            var angle = -2 * Math.PI / size;
            for (var i = 0; i < n; i += size)
            {
                for (var k = 0; k < half; k++)
                {
                    var wr = Math.Cos(angle * k);
                    var wi = Math.Sin(angle * k);
                    var evenRe = re[i + k];
                    var evenIm = im[i + k];
                    var oddRe = re[i + k + half];
                    var oddIm = im[i + k + half];
                    var tRe = wr * oddRe - wi * oddIm;
                    var tIm = wr * oddIm + wi * oddRe;
                    re[i + k] = evenRe + tRe;
                    im[i + k] = evenIm + tIm;
                    re[i + k + half] = evenRe - tRe;
                    im[i + k + half] = evenIm - tIm;
                }
            }
        }
    }

    private static int BitReverse(int value, int bits)
    {
        var reversed = 0;
        for (var i = 0; i < bits; i++)
        {
            reversed = (reversed << 1) | (value & 1);
            value >>= 1;
        }

        return reversed;
    }
}
