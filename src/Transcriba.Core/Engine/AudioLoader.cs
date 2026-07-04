using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Transcriba.Core.Engine;

public sealed class AudioLoader
{
    public const int SampleRate = 16000;

    private readonly FfmpegLocator _ffmpegLocator;

    public AudioLoader(FfmpegLocator ffmpegLocator)
    {
        _ffmpegLocator = ffmpegLocator;
    }

    public float[] LoadSamples16kHz(string inputPath)
    {
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();

        if (ext == ".wav")
        {
            using var reader = new WaveFileReader(inputPath);
            var resampled = new WdlResamplingSampleProvider(reader.ToSampleProvider(), SampleRate);
            return ReadSamples(resampled);
        }

        if (ext == ".mp3")
        {
            using var reader = new Mp3FileReader(inputPath);
            var resampled = new WdlResamplingSampleProvider(reader.ToSampleProvider(), SampleRate);
            return ReadSamples(resampled);
        }

        return LoadSamplesWithFfmpeg(inputPath);
    }

    internal static float[] ReadSamples(ISampleProvider provider)
    {
        var buffer = new List<float>(capacity: 1024 * 1024);
        var chunk = new float[16384];
        int read;
        while ((read = provider.Read(chunk, 0, chunk.Length)) > 0)
            buffer.AddRange(chunk.AsSpan(0, read));
        return CollectionsMarshal.AsSpan(buffer).ToArray();
    }

    private float[] LoadSamplesWithFfmpeg(string inputPath)
    {
        var ffmpeg = _ffmpegLocator.EnsureFfmpeg();

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-nostdin -threads 0 -i \"{inputPath}\" -ar {SampleRate} -ac 1 -f s16le pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Não foi possível iniciar o ffmpeg.");

        var stderrTask = process.StandardError.ReadToEndAsync();
        using var stdout = process.StandardOutput.BaseStream;
        using var pcmStream = new MemoryStream();
        stdout.CopyTo(pcmStream);
        var stderr = stderrTask.GetAwaiter().GetResult();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg falhou:\n{stderr}");

        if (pcmStream.TryGetBuffer(out var segment))
            return ConvertPcm16ToFloat(segment.AsSpan());

        return ConvertPcm16ToFloat(pcmStream.ToArray());
    }

    public static float[] ConvertPcm16ToFloat(ReadOnlySpan<byte> pcmBytes)
    {
        if (pcmBytes.Length < 2)
            throw new InvalidOperationException("ffmpeg não retornou áudio.");

        var pcm = MemoryMarshal.Cast<byte, short>(pcmBytes);
        var samples = new float[pcm.Length];
        for (var i = 0; i < pcm.Length; i++)
            samples[i] = pcm[i] / 32768f;

        return samples;
    }
}
