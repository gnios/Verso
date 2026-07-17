using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Engine.Worker;

namespace Verso.Bench;

// Benchmark da Fase 3 (spike faster-whisper/CTranslate2 — ver
// .specs/features/transcricao-cpu-responsiva/phase3-spike.md, seção R3.3).
//
// Compara, no MESMO hardware/áudios, o motor atual da Verso (whisper.net, rodado pelo caminho de
// produção real: Verso.Worker.exe via WorkerProcessTranscriptionEngine) contra whisper_ctranslate2.exe.
// Coleta RTF, wall-clock e pico de RAM por processo, e imprime a tabela + o gate G1 do spike.
//
// Este processo NÃO deve ter VERSO_WHISPER_N_THREADS setado (senão sobrepõe --threads no motor A).
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        BenchConfig cfg;
        try
        {
            cfg = BenchConfig.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"erro: {ex.Message}\n");
            Console.Error.WriteLine(BenchConfig.Usage);
            return 2;
        }

        if (cfg.ShowHelp)
        {
            Console.WriteLine(BenchConfig.Usage);
            return 0;
        }

        var files = cfg.ResolveAudioFiles();
        if (files.Count == 0)
        {
            Console.Error.WriteLine("erro: nenhum arquivo de áudio encontrado (use --audio-dir ou --audio).");
            return 2;
        }

        var ffmpeg = new FfmpegLocator().FindFfmpeg();
        if (ffmpeg is null)
            Console.Error.WriteLine("aviso: ffmpeg não encontrado no PATH — duração/RTF ficarão indisponíveis.");

        Console.WriteLine($"Verso.Bench — Fase 3 (CTranslate2 spike)");
        Console.WriteLine($"  engines : {(cfg.RunWhisperNet ? "whisper.net " : "")}{(cfg.RunCt2 ? "ctranslate2" : "")}");
        Console.WriteLine($"  threads : {(cfg.Threads > 0 ? cfg.Threads.ToString() : $"auto (Fase 1 = ProcessorCount/2 = {Math.Max(1, Environment.ProcessorCount / 2)})")}");
        Console.WriteLine($"  runs    : {cfg.Runs} medidas + {cfg.Warmup} warmup (descartado)");
        Console.WriteLine($"  arquivos: {files.Count}");
        Console.WriteLine();

        var results = new List<EngineFileResult>();

        foreach (var file in files)
        {
            var durationSec = ffmpeg is not null ? await ProbeDurationSecondsAsync(ffmpeg, file) : (double?)null;
            var shortName = Path.GetFileName(file);
            Console.WriteLine($"── {shortName}  ({(durationSec is { } d ? FormatDuration(d) : "duração ?")})");

            if (cfg.RunWhisperNet)
                results.Add(await MeasureAsync("whisper.net", "Verso.Worker", shortName, durationSec, cfg,
                    () => RunWhisperNetAsync(file, cfg)));

            if (cfg.RunCt2)
                results.Add(await MeasureAsync("ctranslate2", "whisper_ctranslate2", shortName, durationSec, cfg,
                    () => RunCt2Async(file, cfg)));

            Console.WriteLine();
        }

        var report = BuildReport(cfg, results);
        Console.WriteLine(report);

        await File.WriteAllTextAsync(cfg.OutPath, report);
        Console.WriteLine($"\nRelatório salvo em: {cfg.OutPath}");
        return 0;
    }

    // ---- Medição (warmup + N runs, mediana) com amostragem de RAM por nome de processo ----

    private static async Task<EngineFileResult> MeasureAsync(
        string engine, string processName, string file, double? durationSec,
        BenchConfig cfg, Func<Task<RunOutcome>> runOnce)
    {
        for (var i = 0; i < cfg.Warmup; i++)
        {
            Console.Write($"   {engine}: warmup {i + 1}/{cfg.Warmup} … ");
            var w = await SafeRunAsync(processName, runOnce);
            Console.WriteLine(w.Error is null ? $"{w.Wall.TotalSeconds:F1}s" : $"ERRO ({w.Error})");
            if (w.Error is not null)
                return EngineFileResult.Failed(engine, file, durationSec, w.Error);
        }

        var samples = new List<RunSample>();
        for (var i = 0; i < cfg.Runs; i++)
        {
            Console.Write($"   {engine}: run {i + 1}/{cfg.Runs} … ");
            var s = await SafeRunAsync(processName, runOnce);
            if (s.Error is not null)
            {
                Console.WriteLine($"ERRO ({s.Error})");
                return EngineFileResult.Failed(engine, file, durationSec, s.Error);
            }

            samples.Add(s);
            var rtf = durationSec is { } d && d > 0 ? s.Wall.TotalSeconds / d : double.NaN;
            Console.WriteLine($"{s.Wall.TotalSeconds:F1}s  RTF={FormatRtf(rtf)}  RAM={s.PeakRamBytes / (1024.0 * 1024):F0}MB  segs={s.Segments}");
        }

        var medianWall = Median(samples.Select(s => s.Wall.Ticks).ToList());
        var medianRtf = durationSec is { } dur && dur > 0 ? TimeSpan.FromTicks(medianWall).TotalSeconds / dur : double.NaN;
        return new EngineFileResult(
            engine, file, durationSec,
            TimeSpan.FromTicks(medianWall), medianRtf,
            samples.Max(s => s.PeakRamBytes),
            samples[0].Segments, null);
    }

    private static async Task<RunSample> SafeRunAsync(string processName, Func<Task<RunOutcome>> runOnce)
    {
        using var sampler = new PeakRamSampler(processName);
        var sw = Stopwatch.StartNew();
        try
        {
            var outcome = await runOnce();
            sw.Stop();
            return new RunSample(sw.Elapsed, Math.Max(sampler.Stop(), outcome.SelfReportedRamBytes), outcome.Segments, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            sampler.Stop();
            return new RunSample(sw.Elapsed, 0, 0, Summarize(ex.Message));
        }
    }

    // ---- Motor A: whisper.net, caminho de produção real (Verso.Worker.exe) ----

    private static async Task<RunOutcome> RunWhisperNetAsync(string file, BenchConfig cfg)
    {
        var engine = new WorkerProcessTranscriptionEngine(
            new FixedLocator(cfg.WorkerExe), new WorkerProcessFactory());

        var request = new TranscriptionJobRequest(
            Guid.NewGuid(), file, cfg.Language, cfg.Quality, ExecutionDevice.Cpu, cfg.Threads);

        var result = await engine.TranscribeAsync(request, progress: null, CancellationToken.None);
        return new RunOutcome(result.Segments.Count, 0);
    }

    // ---- Motor B: whisper_ctranslate2.exe ----

    private static async Task<RunOutcome> RunCt2Async(string file, BenchConfig cfg)
    {
        var outDir = Path.Combine(Path.GetTempPath(), "verso-bench-ct2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var psi = new ProcessStartInfo(cfg.Ct2Exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(file);
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(cfg.Ct2Model);
            psi.ArgumentList.Add("--compute_type");
            psi.ArgumentList.Add("int8");
            psi.ArgumentList.Add("--device");
            psi.ArgumentList.Add("cpu");
            psi.ArgumentList.Add("--output_format");
            psi.ArgumentList.Add("json");
            psi.ArgumentList.Add("--output_dir");
            psi.ArgumentList.Add(outDir);
            psi.ArgumentList.Add("--language");
            psi.ArgumentList.Add(cfg.Language);
            if (cfg.Threads > 0)
            {
                psi.ArgumentList.Add("--threads");
                psi.ArgumentList.Add(cfg.Threads.ToString(CultureInfo.InvariantCulture));
            }

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"não foi possível iniciar {cfg.Ct2Exe}");

            var stderrTask = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"exit {process.ExitCode}: {Tail(stderr)}");

            var segments = CountSegmentsFromJson(outDir, file);
            return new RunOutcome(segments, 0);
        }
        finally
        {
            TryDeleteDir(outDir);
        }
    }

    private static int CountSegmentsFromJson(string outDir, string audioFile)
    {
        var jsonPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(audioFile) + ".json");
        if (!File.Exists(jsonPath))
        {
            // fallback: pega o primeiro .json produzido
            jsonPath = Directory.EnumerateFiles(outDir, "*.json").FirstOrDefault()
                ?? throw new InvalidOperationException("ctranslate2 não produziu arquivo JSON de saída");
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        return doc.RootElement.TryGetProperty("segments", out var segs) && segs.ValueKind == JsonValueKind.Array
            ? segs.GetArrayLength()
            : 0;
    }

    // ---- Duração via ffmpeg (parseia "Duration: HH:MM:SS.ss" do stderr) ----

    private static async Task<double?> ProbeDurationSecondsAsync(string ffmpeg, string file)
    {
        try
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(file);

            using var process = Process.Start(psi)!;
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var marker = stderr.IndexOf("Duration:", StringComparison.Ordinal);
            if (marker < 0)
                return null;

            var value = stderr[(marker + "Duration:".Length)..].TrimStart();
            value = value[..value.IndexOf(',')].Trim();
            var parts = value.Split(':');
            return int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600
                + int.Parse(parts[1], CultureInfo.InvariantCulture) * 60
                + double.Parse(parts[2], CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    // ---- Relatório ----

    private static string BuildReport(BenchConfig cfg, List<EngineFileResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Fase 3 — Resultados do benchmark (whisper.net vs whisper_ctranslate2)");
        sb.AppendLine();
        sb.AppendLine($"- **Threads**: {(cfg.Threads > 0 ? cfg.Threads.ToString() : $"auto (Fase 1 = {Math.Max(1, Environment.ProcessorCount / 2)})")}");
        sb.AppendLine($"- **whisper.net quality**: `{cfg.Quality}` · **ct2 model**: `{cfg.Ct2Model}` · **compute_type**: `int8` · **device**: `cpu`");
        sb.AppendLine($"- **Idioma**: `{cfg.Language}` · **Runs**: {cfg.Runs} (mediana) + {cfg.Warmup} warmup · **CPU lógicas**: {Environment.ProcessorCount}");
        sb.AppendLine($"- **OS**: {Environment.OSVersion} · gerado por `Verso.Bench`");
        sb.AppendLine();
        sb.AppendLine("| Arquivo | Duração | Engine | RTF (mediana) | Wall (mediana) | Pico RAM | Segments | Erro |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var r in results)
        {
            sb.AppendLine($"| {Path.GetFileName(r.File)} " +
                $"| {(r.DurationSec is { } d ? FormatDuration(d) : "?")} " +
                $"| {r.Engine} " +
                $"| {FormatRtf(r.MedianRtf)} " +
                $"| {(r.Error is null ? $"{r.MedianWall.TotalSeconds:F1}s" : "—")} " +
                $"| {(r.Error is null && r.PeakRamBytes > 0 ? $"{r.PeakRamBytes / (1024.0 * 1024):F0}MB" : "—")} " +
                $"| {(r.Error is null ? r.Segments.ToString() : "—")} " +
                $"| {r.Error ?? ""} |");
        }
        sb.AppendLine();

        // Gate G1: ct2 RTF <= 70% do whisper.net RTF (>= ~1,4x mais rápido).
        sb.AppendLine("## Gate G1 (ganho significativo — ver phase3-spike.md)");
        sb.AppendLine();
        sb.AppendLine("> Adotar exige RTF do ctranslate2 ≤ 70% do RTF do whisper.net (≥ ~1,4× mais rápido).");
        sb.AppendLine();
        sb.AppendLine("| Arquivo | RTF whisper.net | RTF ctranslate2 | Speedup | G1 (≥1,4×) |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var group in results.GroupBy(r => r.File))
        {
            var wn = group.FirstOrDefault(r => r.Engine == "whisper.net" && r.Error is null);
            var ct = group.FirstOrDefault(r => r.Engine == "ctranslate2" && r.Error is null);
            if (wn is null || ct is null || double.IsNaN(wn.MedianRtf) || double.IsNaN(ct.MedianRtf) || ct.MedianRtf <= 0)
            {
                sb.AppendLine($"| {Path.GetFileName(group.Key)} | {(wn is null ? "—" : FormatRtf(wn.MedianRtf))} | {(ct is null ? "—" : FormatRtf(ct.MedianRtf))} | — | dados insuficientes |");
                continue;
            }

            var speedup = wn.MedianRtf / ct.MedianRtf;
            var pass = speedup >= 1.4;
            sb.AppendLine($"| {Path.GetFileName(group.Key)} | {FormatRtf(wn.MedianRtf)} | {FormatRtf(ct.MedianRtf)} | {speedup:F2}× | {(pass ? "✅ passa" : "❌ falha")} |");
        }
        sb.AppendLine();
        sb.AppendLine("Preencher também os gates G2–G5 (qualidade, estabilidade, tamanho do instalador, Vulkan) manualmente — ver `phase3-spike.md`.");
        return sb.ToString();
    }

    // ---- Utilidades ----

    private static long Median(List<long> values)
    {
        values.Sort();
        var n = values.Count;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
    }

    private static string FormatRtf(double rtf) => double.IsNaN(rtf) ? "?" : $"{rtf:F3}";

    private static string FormatDuration(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    private static string Tail(string s, int max = 200) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s.Trim() : "…" + s[^max..].Trim();

    private static string Summarize(string msg) => msg.Replace('\n', ' ').Replace('\r', ' ').Trim() is { Length: > 120 } m
        ? m[..120] + "…"
        : msg.Replace('\n', ' ').Replace('\r', ' ').Trim();

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }
}

/// <summary>Locator fixo para apontar o benchmark a um Verso.Worker.exe específico.</summary>
internal sealed class FixedLocator(string path) : IWorkerExecutableLocator
{
    public string Resolve() => File.Exists(path)
        ? path
        : throw new FileNotFoundException($"Verso.Worker.exe não encontrado em '{path}' (use --worker-exe).", path);
}

internal readonly record struct RunOutcome(int Segments, long SelfReportedRamBytes);

internal readonly record struct RunSample(TimeSpan Wall, long PeakRamBytes, int Segments, string? Error);

internal sealed record EngineFileResult(
    string Engine, string File, double? DurationSec,
    TimeSpan MedianWall, double MedianRtf, long PeakRamBytes, int Segments, string? Error)
{
    public static EngineFileResult Failed(string engine, string file, double? durationSec, string error) =>
        new(engine, file, durationSec, TimeSpan.Zero, double.NaN, 0, 0, error);
}

/// <summary>
/// Amostra o pico de RAM (WorkingSet64) dos processos com o nome dado, a cada ~100ms enquanto o
/// job roda. Funciona para os dois motores (worker whisper.net e whisper_ctranslate2), já que ambos
/// executam como processos filhos nomeados.
/// </summary>
internal sealed class PeakRamSampler : IDisposable
{
    private readonly string _processName;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _task;
    private long _peakBytes;

    public PeakRamSampler(string processName)
    {
        _processName = processName;
        _task = Task.Run(SampleLoopAsync);
    }

    private async Task SampleLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                long sum = 0;
                foreach (var p in Process.GetProcessesByName(_processName))
                {
                    using (p)
                    {
                        p.Refresh();
                        sum += p.WorkingSet64;
                    }
                }

                if (sum > _peakBytes)
                    _peakBytes = sum;
            }
            catch
            {
                // processos podem sair no meio da enumeração — ignora e continua amostrando
            }

            try { await Task.Delay(100, _cts.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    public long Stop()
    {
        _cts.Cancel();
        try { _task.Wait(TimeSpan.FromSeconds(2)); } catch { /* best-effort */ }
        return _peakBytes;
    }

    public void Dispose()
    {
        if (!_cts.IsCancellationRequested)
            Stop();
        _cts.Dispose();
    }
}

internal sealed class BenchConfig
{
    public bool ShowHelp { get; private init; }
    public List<string> AudioPaths { get; } = [];
    public string? AudioDir { get; private set; }
    public string WorkerExe { get; private set; } = "";
    public string Ct2Exe { get; private set; } = "";
    public ModelQuality Quality { get; private set; } = ModelQuality.Standard;
    public string Ct2Model { get; private set; } = "small";
    public string Language { get; private set; } = "pt";
    public int Threads { get; private set; }
    public int Runs { get; private set; } = 3;
    public int Warmup { get; private set; } = 1;
    public string OutPath { get; private set; } = "bench-results.md";

    public bool RunWhisperNet { get; private set; } = true;
    public bool RunCt2 { get; private set; } = true;

    public const string Usage = """
        Verso.Bench — benchmark Fase 3 (whisper.net vs whisper_ctranslate2)

        Uso:
          dotnet run --project bench/Verso.Bench -- [opções]

        Fontes de áudio (uma obrigatória):
          --audio-dir <dir>     pasta com arquivos de áudio/vídeo
          --audio <arquivo>     um arquivo (pode repetir)

        Motores:
          --worker-exe <path>   Verso.Worker.exe (motor A whisper.net). Obrigatório se A ligado.
          --ct2-exe <path>      whisper_ctranslate2.exe (motor B). Obrigatório se B ligado.
          --engines <a,b>       quais rodar (default: a,b). Ex.: --engines a  (só whisper.net)

        Parâmetros de transcrição:
          --quality <q>         ModelQuality do whisper.net (default Standard = small ggml).
                                Ex.: Standard, Base, Medium, LargeV3Turbo
          --ct2-model <m>       modelo do ctranslate2 (default small). Ex.: small, base, medium, large-v3
          --language <lang>     idioma (default pt)
          --threads <N>         N threads para AMBOS (default 0 = auto Fase 1 = ProcessorCount/2).
                                Para comparação justa, passe o mesmo N > 0 explícito.

        Amostragem:
          --runs <N>            execuções medidas por arquivo/motor (default 3, usa a mediana)
          --warmup <N>          execuções de aquecimento descartadas (default 1; cobre o download
                                do modelo do ctranslate2 na 1ª vez)

        Saída:
          --out <arquivo>       relatório markdown (default bench-results.md)
          -h, --help            esta ajuda

        Observações:
          * NÃO deixe VERSO_WHISPER_N_THREADS setado (ele sobrepõe --threads no motor A).
          * Rode no MESMO hardware Windows onde a UI travava (ver phase3-spike.md, R3.3).
        """;

    public static BenchConfig Parse(string[] args)
    {
        var cfg = new BenchConfig();
        var engines = "a,b";

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next(string name) => i + 1 < args.Length
                ? args[++i]
                : throw new ArgumentException($"faltou valor para {name}");

            switch (arg)
            {
                case "-h" or "--help": return new BenchConfig { ShowHelp = true };
                case "--audio-dir": cfg.AudioDir = Next(arg); break;
                case "--audio": cfg.AudioPaths.Add(Next(arg)); break;
                case "--worker-exe": cfg.WorkerExe = Next(arg); break;
                case "--ct2-exe": cfg.Ct2Exe = Next(arg); break;
                case "--quality": cfg.Quality = Enum.Parse<ModelQuality>(Next(arg), ignoreCase: true); break;
                case "--ct2-model": cfg.Ct2Model = Next(arg); break;
                case "--language": cfg.Language = Next(arg); break;
                case "--threads": cfg.Threads = ParseInt(Next(arg), arg); break;
                case "--runs": cfg.Runs = Math.Max(1, ParseInt(Next(arg), arg)); break;
                case "--warmup": cfg.Warmup = Math.Max(0, ParseInt(Next(arg), arg)); break;
                case "--out": cfg.OutPath = Next(arg); break;
                case "--engines": engines = Next(arg).ToLowerInvariant(); break;
                default: throw new ArgumentException($"opção desconhecida: {arg}");
            }
        }

        cfg.RunWhisperNet = engines.Contains('a');
        cfg.RunCt2 = engines.Contains('b');

        if (!cfg.ShowHelp)
        {
            if (cfg.RunWhisperNet && string.IsNullOrEmpty(cfg.WorkerExe))
                throw new ArgumentException("--worker-exe é obrigatório para o motor whisper.net (ou desligue com --engines b)");
            if (cfg.RunCt2 && string.IsNullOrEmpty(cfg.Ct2Exe))
                throw new ArgumentException("--ct2-exe é obrigatório para o motor ctranslate2 (ou desligue com --engines a)");
        }

        return cfg;
    }

    private static int ParseInt(string value, string name) => int.TryParse(value, out var n)
        ? n
        : throw new ArgumentException($"{name} espera um inteiro, recebeu '{value}'");

    public List<string> ResolveAudioFiles()
    {
        var files = new List<string>(AudioPaths.Where(File.Exists));
        if (AudioDir is not null && Directory.Exists(AudioDir))
        {
            string[] exts = [".wav", ".mp3", ".m4a", ".flac", ".ogg", ".opus", ".mp4", ".mkv", ".webm", ".aac"];
            files.AddRange(Directory.EnumerateFiles(AudioDir)
                .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.Ordinal));
        }

        return files.Distinct().ToList();
    }
}
