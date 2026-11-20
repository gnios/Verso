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
    internal const double FfmpegSeekPrerollSeconds = 5;
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

    internal float[] LoadDiarizationSamples16kHz(string inputPath)
    {
        if (Path.GetExtension(inputPath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = OpenSharedRead(inputPath);
            using var reader = new WaveFileReader(stream);
            if (reader.WaveFormat.Channels <= 2)
                return ReadWavSamples(reader, startSeconds: null, durationSeconds: null, mono: true);
        }

        return LoadSamplesWithFfmpeg(inputPath, floatPcm: true);
    }

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
        return ReadWavSamples(reader, startSeconds, durationSeconds, mono: false);
    }

    private static float[] ReadWavSamples(
        WaveFileReader reader, double? startSeconds, double? durationSeconds, bool mono)
    {
        ISampleProvider provider = reader.ToSampleProvider();
        if (mono && reader.WaveFormat.Channels == 2)
            provider = new StereoToMonoSampleProvider(provider);

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
        double? durationSeconds = null,
        bool floatPcm = false)
    {
        var ffmpeg = _ffmpegLocator.EnsureFfmpeg();
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = BuildFfmpegPcmArguments(inputPath, startSeconds, durationSeconds, floatPcm),
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

        if (!pcmStream.TryGetBuffer(out var segment))
            segment = new ArraySegment<byte>(pcmStream.ToArray());

        if (!floatPcm)
            return ConvertPcm16ToFloat(segment.AsSpan());
        if (segment.Count < sizeof(float) || segment.Count % sizeof(float) != 0)
            throw new InvalidOperationException("ffmpeg não retornou PCM de 32 bits válido.");

        return MemoryMarshal.Cast<byte, float>(segment.AsSpan()).ToArray();
    }

    /// <summary>
    /// Seek híbrido: -ss rápido no input (keyframe) + -ss/-t no output para cair
    /// no timestamp pedido. -ss/-t só no input atrasava ou furava janelas em m4a/mp4.
    /// </summary>
    internal static string BuildFfmpegPcmArguments(
        string inputPath,
        double? startSeconds,
        double? durationSeconds,
        bool floatPcm = false)
    {
        var inv = CultureInfo.InvariantCulture;
        var quoted = $"\"{inputPath}\"";
        var prefix = "-nostdin -hide_banner -loglevel error -threads 0";
        var suffix = floatPcm
            ? $"-vn -ac 1 -ar {SampleRate} -f f32le pipe:1"
            : $"-ar {SampleRate} -ac 1 -f s16le pipe:1";

        if (startSeconds is >= 0)
        {
            var start = startSeconds.Value;
            var preroll = Math.Min(FfmpegSeekPrerollSeconds, start);
            var inputSeek = (start - preroll).ToString(inv);
            var outputSeek = preroll.ToString(inv);
            var take = durationSeconds is > 0
                ? $" -t {durationSeconds.Value.ToString(inv)}"
                : "";
            return $"{prefix} -ss {inputSeek} -i {quoted} -ss {outputSeek}{take} {suffix}";
        }

        var takeOnly = durationSeconds is > 0
            ? $" -t {durationSeconds.Value.ToString(inv)}"
            : "";
        return $"{prefix} -i {quoted}{takeOnly} {suffix}";
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
    /// Picos de waveform em O(1) de memória: ffmpeg decodifica para s16le 8kHz mono
    /// via pipe e cada amostra alimenta o máximo do seu bucket — sem materializar o
    /// áudio (um m4a de 1h virava ~1GB de PCM no WebView e derrubava o renderer).
    /// <paramref name="durationSeconds"/> vem do cadastro da transcrição; se zerado,
    /// tenta <see cref="GetDuration"/> (ffprobe).
    /// </summary>
    public float[] ComputePeaks(
        string inputPath,
        int buckets,
        double durationSeconds = 0,
        CancellationToken cancellationToken = default)
    {
        const int peaksRate = 8000;
        if (buckets <= 0 || buckets > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(buckets));
        }

        var seconds = durationSeconds > 0 ? durationSeconds : GetDuration(inputPath);
        if (seconds <= 0)
        {
            throw new InvalidOperationException("Duração desconhecida — não foi possível gerar picos.");
        }

        var ffmpeg = _ffmpegLocator.EnsureFfmpeg();
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-nostdin -hide_banner -loglevel error -i \"{inputPath}\" -ar {peaksRate} -ac 1 -f s16le pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Não foi possível iniciar o ffmpeg.");
        var stderrTask = process.StandardError.ReadToEndAsync();

        var peaks = new float[buckets];
        var samplesPerBucket = Math.Max(1.0, seconds * peaksRate / buckets);
        long sampleIndex = 0;
        var carry = -1; // byte baixo pendente quando o chunk corta um sample s16 no meio

        using (var stdout = process.StandardOutput.BaseStream)
        {
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stdout.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var i = 0; i < read; i++)
                {
                    if (carry < 0)
                    {
                        carry = buffer[i];
                        continue;
                    }

                    var sample = (short)(carry | (buffer[i] << 8));
                    carry = -1;
                    var bucket = (int)(sampleIndex / samplesPerBucket);
                    if (bucket >= buckets)
                    {
                        bucket = buckets - 1;
                    }

                    var abs = Math.Abs(sample / 32768f);
                    if (abs > peaks[bucket])
                    {
                        peaks[bucket] = abs;
                    }

                    sampleIndex++;
                }
            }
        }

        var stderr = stderrTask.GetAwaiter().GetResult();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg falhou:\n{stderr}");
        }

        return peaks;
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
