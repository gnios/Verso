using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Verso.Core.Engine;

public sealed class AudioLoader
{
    public const int SampleRate = 16000;
    private static readonly Regex FfmpegDurationRegex = new(
        @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly FfmpegLocator _ffmpegLocator;

    public AudioLoader(FfmpegLocator ffmpegLocator)
    {
        _ffmpegLocator = ffmpegLocator;
    }

    public float[] LoadSamples16kHz(string inputPath) =>
        LoadSamples16kHz(inputPath, startSeconds: null, durationSeconds: null);

    public float[] LoadSamples16kHz(string inputPath, double startSeconds, double durationSeconds) =>
        LoadSamples16kHz(inputPath, (double?)startSeconds, (double?)durationSeconds);

    private float[] LoadSamples16kHz(string inputPath, double? startSeconds, double? durationSeconds)
    {
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();

        if (ext == ".wav")
        {
            return LoadWavSamples(inputPath, startSeconds, durationSeconds);
        }

        return LoadSamplesWithFfmpeg(inputPath, startSeconds, durationSeconds);
    }

    private static float[] LoadWavSamples(string inputPath, double? startSeconds, double? durationSeconds)
    {
        using var stream = OpenSharedRead(inputPath);
        using var reader = new WaveFileReader(stream);
        ISampleProvider provider = reader.ToSampleProvider();
        if (startSeconds is >= 0 || durationSeconds is > 0)
        {
            var offset = new OffsetSampleProvider(provider);
            if (startSeconds is >= 0)
            {
                offset.SkipOver = TimeSpan.FromSeconds(startSeconds.Value);
            }

            if (durationSeconds is > 0)
            {
                offset.Take = TimeSpan.FromSeconds(durationSeconds.Value);
            }

            provider = offset;
        }

        if (reader.WaveFormat.SampleRate != SampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, SampleRate);
        }

        return ReadSamples(provider);
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

    private float[] LoadSamplesWithFfmpeg(
        string inputPath,
        double? startSeconds = null,
        double? durationSeconds = null)
    {
        var ffmpeg = _ffmpegLocator.EnsureFfmpeg();
        var seek = startSeconds is >= 0
            ? $" -ss {startSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : "";
        var take = durationSeconds is > 0
            ? $" -t {durationSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : "";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-nostdin -hide_banner -loglevel error -threads 0{seek}{take} -i \"{inputPath}\" -ar {SampleRate} -ac 1 -f s16le pipe:1",
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

    private static FileStream OpenSharedRead(string inputPath) =>
        new(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

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

    /// <summary>
    /// Lê só a duração (segundos), sem decodificar o PCM. WAV usa o header;
    /// demais formatos tentam ffprobe e, se faltar, o banner do ffmpeg (<c>-i</c> sem decode).
    /// </summary>
    public double GetDuration(string inputPath)
    {
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();
        if (ext == ".wav")
        {
            try
            {
                using var stream = OpenSharedRead(inputPath);
                using var reader = new WaveFileReader(stream);
                return reader.TotalTime.TotalSeconds;
            }
            catch
            {
                return 0;
            }
        }

        return ProbeDurationWithFfprobe(inputPath) ?? ProbeDurationWithFfmpeg(inputPath) ?? 0;
    }

    private double? ProbeDurationWithFfprobe(string inputPath)
    {
        try
        {
            var ffmpeg = _ffmpegLocator.EnsureFfmpeg();
            var dir = Path.GetDirectoryName(ffmpeg);
            if (string.IsNullOrEmpty(dir))
            {
                return null;
            }

            var ffprobePath = Path.Combine(dir, "ffprobe.exe");
            if (!File.Exists(ffprobePath))
            {
                ffprobePath = Path.Combine(dir, "ffprobe");
            }

            if (!File.Exists(ffprobePath))
            {
                return null;
            }

            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v error -show_entries format=duration -of csv=p=0 \"{inputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode == 0 &&
                double.TryParse(output, NumberStyles.Any, CultureInfo.InvariantCulture, out var duration) &&
                duration > 0)
            {
                return duration;
            }
        }
        catch
        {
            // ffprobe ausente ou arquivo inválido
        }

        return null;
    }

    private double? ProbeDurationWithFfmpeg(string inputPath)
    {
        try
        {
            var ffmpeg = _ffmpegLocator.EnsureFfmpeg();
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-hide_banner -i \"{inputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            var match = FfmpegDurationRegex.Match(stderr);
            if (!match.Success)
            {
                return null;
            }

            var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            var duration = (hours * 3600) + (minutes * 60) + seconds;
            return duration > 0 ? duration : null;
        }
        catch
        {
            return null;
        }
    }
}
