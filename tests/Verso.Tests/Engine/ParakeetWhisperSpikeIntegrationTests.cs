using System.Diagnostics;
using System.Text;
using NAudio.Wave;
using Verso.Core;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;

namespace Verso.Tests.Engine;

/// <summary>
/// Spike STT-08: compara RTF (e WER se houver transcrição de referência) de Parakeet vs Whisper Small.
/// Excluído do gate padrão pelo nome Integration. Roda só com VERSO_PARAKEET_SPIKE=1 e modelo em disco
/// (download ~670 MB). Gera um WAV pt-BR via `say` no macOS quando disponível.
/// </summary>
public class ParakeetWhisperSpikeIntegrationTests
{
    private const string Reference = "olá este é um teste de transcrição em português brasileiro";

    [Fact]
    public async Task Spike_ParakeetInt8_ReportsRtfAgainstWhisperSmallWhenEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("VERSO_PARAKEET_SPIKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var modelsDir = VersoPaths.ModelsDirectory;
        var parakeetDir = Path.Combine(modelsDir, ParakeetModelManager.GetModelDirectoryName(ParakeetModel.MultilingualV3));
        var manager = new ParakeetModelManager();
        await manager.EnsureModelAsync(parakeetDir, ParakeetModel.MultilingualV3);

        var wavPath = await CreatePortugueseFixtureAsync();
        var report = new StringBuilder();
        try
        {
            var audioLoader = new AudioLoader(new FfmpegLocator());
            var samples = audioLoader.LoadSamples16kHz(wavPath);
            var duration = samples.Length / (double)AudioLoader.SampleRate;
            report.AppendLine($"Fixture: {wavPath}");
            report.AppendLine($"DurationSeconds={duration:F3}");
            report.AppendLine($"Reference={Reference}");

            using var recognizer = new OnnxParakeetRecognizer(parakeetDir, threads: Math.Max(1, Environment.ProcessorCount / 2));
            var sw = Stopwatch.StartNew();
            var parakeetSegments = recognizer.Recognize(samples);
            sw.Stop();
            var parakeetText = string.Join(" ", parakeetSegments.Select(s => s.Text));
            var parakeetRtf = sw.Elapsed.TotalSeconds / Math.Max(duration, 0.001);
            var parakeetWer = WordErrorRate.Compute(Reference, parakeetText);

            Assert.False(string.IsNullOrWhiteSpace(parakeetText));
            Assert.True(parakeetRtf > 0);
            Assert.All(parakeetSegments, s =>
            {
                Assert.True(s.EndSeconds > s.StartSeconds);
                Assert.False(string.IsNullOrWhiteSpace(s.Text));
            });

            report.AppendLine($"Parakeet v3 INT8: RTF={parakeetRtf:F3} WER={parakeetWer:F3} segments={parakeetSegments.Count} text={parakeetText}");

            var whisperLine = await TryRunWhisperSmallAsync(audioLoader, wavPath, modelsDir, duration);
            report.AppendLine(whisperLine);

            var outputPath = Path.Combine(Path.GetTempPath(), "verso-parakeet-spike.txt");
            File.WriteAllText(outputPath, report.ToString());
            var specPath = FindSpikeResultsPath();
            if (specPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(specPath)!);
                File.WriteAllText(specPath, "# Spike STT-08 — Parakeet INT8 vs Whisper Small CPU\n\n" + report);
            }
        }
        finally
        {
            if (File.Exists(wavPath))
            {
                File.Delete(wavPath);
            }
        }
    }

    private static async Task<string> TryRunWhisperSmallAsync(
        AudioLoader audioLoader,
        string wavPath,
        string modelsDir,
        double duration)
    {
        var whisperPath = Path.Combine(modelsDir, ModelManager.GetModelFileName(ModelQuality.Standard));
        var modelManager = new ModelManager();
        await modelManager.EnsureModelAsync(whisperPath, ModelQuality.Standard);

        using var whisper = new WhisperTranscriptionEngine(
            audioLoader,
            modelManager,
            modelsDirectory: modelsDir);
        var warmupRequest = new TranscriptionJobRequest(
            Guid.NewGuid(),
            wavPath,
            "pt",
            ModelQuality.Standard,
            ExecutionDevice.Cpu);
        await whisper.TranscribeAsync(warmupRequest, progress: null, CancellationToken.None);
        var sw = Stopwatch.StartNew();
        var result = await whisper.TranscribeAsync(warmupRequest, progress: null, CancellationToken.None);
        sw.Stop();
        var text = string.Join(" ", result.Segments.Select(s => s.Text));
        var rtf = sw.Elapsed.TotalSeconds / Math.Max(duration, 0.001);
        var wer = WordErrorRate.Compute(Reference, text);
        return $"Whisper Small CPU: RTF={rtf:F3} WER={wer:F3} segments={result.Segments.Count} text={text}";
    }

    private static string? FindSpikeResultsPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, ".specs", "features", "stt-parakeet-cpu");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(candidate, "spike-results.md");
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    private static async Task<string> CreatePortugueseFixtureAsync()
    {
        var wavPath = Path.Combine(Path.GetTempPath(), $"verso-spike-{Guid.NewGuid():N}.wav");
        if (OperatingSystem.IsMacOS())
        {
            var aiff = Path.ChangeExtension(wavPath, ".aiff");
            var start = new ProcessStartInfo("say")
            {
                ArgumentList =
                {
                    "-v", "Luciana",
                    "-o", aiff,
                    "Olá, este é um teste de transcrição em português brasileiro.",
                },
                RedirectStandardError = true,
            };
            using var process = Process.Start(start);
            if (process is not null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0 && File.Exists(aiff) && new FileInfo(aiff).Length > 1000)
                {
                    var ffmpeg = new ProcessStartInfo("ffmpeg")
                    {
                        ArgumentList = { "-y", "-i", aiff, "-ac", "1", "-ar", "16000", "-sample_fmt", "s16", wavPath },
                        RedirectStandardError = true,
                    };
                    using var convert = Process.Start(ffmpeg);
                    if (convert is not null)
                    {
                        await convert.WaitForExitAsync();
                        File.Delete(aiff);
                        if (convert.ExitCode == 0 && File.Exists(wavPath))
                        {
                            return wavPath;
                        }
                    }
                }
            }
        }

        using var writer = new WaveFileWriter(wavPath, new WaveFormat(16000, 16, 1));
        var silence = new byte[16000 * 2];
        writer.Write(silence, 0, silence.Length);
        return wavPath;
    }
}
