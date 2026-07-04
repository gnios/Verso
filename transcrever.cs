#:package Whisper.net.AllRuntimes@1.9.1
#:package NAudio@2.3.0

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine(new string('=', 50));
Console.WriteLine("  Teste de transcrição — Whisper.net");
Console.WriteLine(new string('=', 50));

var arquivoAudio = PedirCaminhoAudio();
var (modelo, ggmlType) = PedirModelo();
var dispositivo = PedirDispositivo();

Console.WriteLine();
Console.WriteLine($"Arquivo: {arquivoAudio}");
Console.WriteLine($"Modelo:  {modelo}");
Console.WriteLine($"Device:  {dispositivo}");

ConfigurarRuntime(dispositivo);

var modelsDir = Path.Combine(Directory.GetCurrentDirectory(), "models");
Directory.CreateDirectory(modelsDir);
var modelPath = Path.Combine(modelsDir, ObterNomeModelo(ggmlType));

var total = Stopwatch.StartNew();

Console.WriteLine("\nCarregando modelo e áudio em paralelo...");
var swModelo = Stopwatch.StartNew();
await GarantirModeloAsync(modelPath, ggmlType);
var audioTask = Task.Run(() => CarregarSamples16kHz(arquivoAudio));
using var whisperFactory = WhisperFactory.FromPath(modelPath);
var samples = await audioTask;
swModelo.Stop();
Console.WriteLine($"Modelo carregado em {FormatarTempo(swModelo.Elapsed)}.");
Console.WriteLine($"Runtime ativo: {RuntimeOptions.LoadedLibrary}");

Console.WriteLine("\nPreparando áudio...");
var swAudio = Stopwatch.StartNew();
var trechosSilencio = DividirPorSilencio(samples);
var (maxPartes, paralelismo, threadsPorJob) = CalcularLimitesParalelos(dispositivo);
var partes = AgruparPartes(trechosSilencio, maxPartes);
swAudio.Stop();
Console.WriteLine(
    $"Áudio preparado em {FormatarTempo(swAudio.Elapsed)}.\n" +
    $"  Trechos por silêncio: {trechosSilencio.Count}\n" +
    $"  Partes para transcrição: {partes.Count}\n" +
    $"  Paralelismo: {paralelismo} | Threads/job: {threadsPorJob} | CPU: {Environment.ProcessorCount}");

var swIdioma = Stopwatch.StartNew();
var idioma = DetectarIdioma(whisperFactory, samples, threadsPorJob);
swIdioma.Stop();
Console.WriteLine($"Idioma detectado: {idioma} ({FormatarTempo(swIdioma.Elapsed)})");

Console.WriteLine("\nTranscrevendo (paralelo)...");
var swTranscricao = Stopwatch.StartNew();
var segmentos = new ConcurrentBag<(int ParteIndex, double InicioSec, double FimSec, string Texto)>();
var progressoLock = new object();

await Parallel.ForEachAsync(
    partes.Select((parte, index) => (Index: index, Parte: parte)),
    new ParallelOptions { MaxDegreeOfParallelism = paralelismo },
    async (item, ct) =>
    {
        var (index, (offset, chunk)) = (item.Index, item.Parte);
        lock (progressoLock)
            Console.WriteLine($"  Parte {index + 1}/{partes.Count} iniciada ({offset:F1}s)...");

        using var processor = CriarProcessor(whisperFactory, idioma, threadsPorJob);

        await foreach (var result in processor.ProcessAsync(chunk, ct))
        {
            segmentos.Add((
                index,
                result.Start.TotalSeconds + offset,
                result.End.TotalSeconds + offset,
                result.Text.Trim()));
            processor.Return(result);
        }

        lock (progressoLock)
            Console.WriteLine($"  Parte {index + 1}/{partes.Count} concluída.");
    });

var linhas = segmentos
    .OrderBy(s => s.ParteIndex)
    .ThenBy(s => s.InicioSec)
    .Select(s => FormatarSegmento(
        TimeSpan.FromSeconds(s.InicioSec),
        TimeSpan.FromSeconds(s.FimSec),
        s.Texto))
    .ToList();

foreach (var linha in linhas)
    Console.WriteLine(linha);

swTranscricao.Stop();
total.Stop();

var saidaTxt = Path.Combine(
    Path.GetDirectoryName(arquivoAudio)!,
    $"{Path.GetFileNameWithoutExtension(arquivoAudio)}_transcricao.txt");

var cabecalho = new[]
{
    $"Arquivo: {Path.GetFileName(arquivoAudio)}",
    $"Modelo: {modelo}",
    $"Device: {dispositivo} ({RuntimeOptions.LoadedLibrary})",
    $"Partes (silêncio→agrupadas): {trechosSilencio.Count}→{partes.Count}",
    $"Paralelismo: {paralelismo} | Threads/job: {threadsPorJob}",
    $"Idioma: {idioma}",
    $"Tempo de transcrição: {FormatarTempo(swTranscricao.Elapsed)}",
    $"Tempo total: {FormatarTempo(total.Elapsed)}",
};

await File.WriteAllLinesAsync(saidaTxt, cabecalho.Concat([""]).Concat(linhas), Encoding.UTF8);

Console.WriteLine();
Console.WriteLine(new string('=', 50));
Console.WriteLine($"Transcrição salva em: {saidaTxt}");
Console.WriteLine($"Tempo de transcrição: {FormatarTempo(swTranscricao.Elapsed)}");
Console.WriteLine($"Tempo total: {FormatarTempo(total.Elapsed)}");
Console.WriteLine(new string('=', 50));

static string PedirCaminhoAudio()
{
    while (true)
    {
        Console.Write("\nCaminho do arquivo de áudio: ");
        var caminho = Console.ReadLine()?.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(caminho))
        {
            Console.WriteLine("Informe um caminho válido.");
            continue;
        }

        caminho = Path.GetFullPath(caminho);
        if (!File.Exists(caminho))
        {
            Console.WriteLine($"Arquivo não encontrado: {caminho}");
            continue;
        }

        return caminho;
    }
}

static (string Nome, GgmlType Tipo) PedirModelo()
{
    var modelos = new (string Nome, GgmlType Tipo)[]
    {
        ("tiny", GgmlType.Tiny),
        ("base", GgmlType.Base),
        ("small", GgmlType.Small),
        ("medium", GgmlType.Medium),
        ("large-v2", GgmlType.LargeV2),
        ("large-v3", GgmlType.LargeV3),
        ("turbo", GgmlType.LargeV3Turbo),
    };

    Console.WriteLine("\nModelos sugeridos:");
    for (var i = 0; i < modelos.Length; i++)
        Console.WriteLine($"  {i + 1}. {modelos[i].Nome}");

    while (true)
    {
        Console.Write("\nDigite o nome do modelo ou o número da lista (padrão: small): ");
        var escolha = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(escolha))
            return modelos[2];

        if (int.TryParse(escolha, out var indice) && indice >= 1 && indice <= modelos.Length)
            return modelos[indice - 1];

        var porNome = modelos.FirstOrDefault(m =>
            m.Nome.Equals(escolha, StringComparison.OrdinalIgnoreCase));
        if (porNome.Nome is not null)
            return porNome;

        Console.WriteLine("Opção inválida.");
    }
}

static string PedirDispositivo()
{
    var opcoes = new (string Codigo, string Nome)[]
    {
        ("cpu", "CPU"),
        ("cuda", "CUDA (NVIDIA)"),
        ("vulkan", "Vulkan"),
    };

    Console.WriteLine("\nDispositivos disponíveis:");
    for (var i = 0; i < opcoes.Length; i++)
        Console.WriteLine($"  {i + 1}. {opcoes[i].Nome}");

    while (true)
    {
        Console.Write("\nEscolha o dispositivo (número ou nome: cpu, cuda, vulkan) (padrão: cuda): ");
        var escolha = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(escolha))
            return "cuda";

        if (int.TryParse(escolha, out var indice) && indice >= 1 && indice <= opcoes.Length)
            return opcoes[indice - 1].Codigo;

        if (opcoes.Any(o => o.Codigo == escolha))
            return escolha;

        Console.WriteLine("Opção inválida. Use cpu, cuda, vulkan ou o número da lista.");
    }
}

static void ConfigurarRuntime(string dispositivo)
{
    RuntimeOptions.RuntimeLibraryOrder = dispositivo switch
    {
        "cpu" => [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx],
        "cuda" => [RuntimeLibrary.Cuda, RuntimeLibrary.Cuda12],
        "vulkan" => [RuntimeLibrary.Vulkan],
        _ => throw new InvalidOperationException($"Dispositivo desconhecido: {dispositivo}"),
    };
}

static string ObterNomeModelo(GgmlType tipo) => tipo switch
{
    GgmlType.Tiny => "ggml-tiny.bin",
    GgmlType.Base => "ggml-base.bin",
    GgmlType.Small => "ggml-small.bin",
    GgmlType.Medium => "ggml-medium.bin",
    GgmlType.LargeV2 => "ggml-large-v2.bin",
    GgmlType.LargeV3 => "ggml-large-v3.bin",
    GgmlType.LargeV3Turbo => "ggml-large-v3-turbo.bin",
    _ => $"ggml-{tipo.ToString().ToLowerInvariant()}.bin",
};

static async Task GarantirModeloAsync(string modelPath, GgmlType ggmlType)
{
    if (File.Exists(modelPath))
        return;

    Console.WriteLine($"Baixando modelo {Path.GetFileName(modelPath)}...");
    await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType);
    await using var fileWriter = File.OpenWrite(modelPath);
    await modelStream.CopyToAsync(fileWriter);
}

const int SampleRate = 16000;
const int MinSilenceMs = 500;
const double SilenceDb = -40.0;
const int FrameMs = 30;
const int MinSpeechMs = 100;
const int IdiomaAmostraSec = 30;

static WhisperProcessor CriarProcessor(WhisperFactory factory, string idioma, int threads) =>
    factory.CreateBuilder()
        .WithLanguage(idioma)
        .WithNoContext()
        .WithGreedySamplingStrategy()
        .WithStringPool()
        .WithThreads(threads)
        .Build();

static string DetectarIdioma(WhisperFactory factory, float[] samples, int threads)
{
    var amostraLen = Math.Min(samples.Length, IdiomaAmostraSec * SampleRate);
    var amostra = amostraLen == samples.Length
        ? samples
        : samples.AsSpan(0, amostraLen).ToArray();

    using var processor = factory.CreateBuilder()
        .WithNoContext()
        .WithGreedySamplingStrategy()
        .WithThreads(Math.Min(threads, 4))
        .Build();

    return processor.DetectLanguage(amostra) ?? "en";
}

static float[] CarregarSamples16kHz(string inputPath)
{
    var ext = Path.GetExtension(inputPath).ToLowerInvariant();

    if (ext == ".wav")
    {
        using var reader = new WaveFileReader(inputPath);
        var resampled = new WdlResamplingSampleProvider(reader.ToSampleProvider(), SampleRate);
        return LerSamples(resampled);
    }

    if (ext == ".mp3")
    {
        using var reader = new Mp3FileReader(inputPath);
        var resampled = new WdlResamplingSampleProvider(reader.ToSampleProvider(), SampleRate);
        return LerSamples(resampled);
    }

    return CarregarSamplesComFfmpeg(inputPath);
}

static float[] LerSamples(ISampleProvider provider)
{
    var buffer = new List<float>(capacity: 1024 * 1024);
    var chunk = new float[16384];
    int read;
    while ((read = provider.Read(chunk, 0, chunk.Length)) > 0)
        buffer.AddRange(chunk.AsSpan(0, read));
    return CollectionsMarshal.AsSpan(buffer).ToArray();
}

static float[] CarregarSamplesComFfmpeg(string inputPath)
{
    var ffmpeg = GarantirFfmpeg();

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
        return ConverterPcm16ParaFloat(segment.AsSpan());

    var bytes = pcmStream.ToArray();
    return ConverterPcm16ParaFloat(bytes);
}

static float[] ConverterPcm16ParaFloat(ReadOnlySpan<byte> pcmBytes)
{
    if (pcmBytes.Length < 2)
        throw new InvalidOperationException("ffmpeg não retornou áudio.");

    var pcm = MemoryMarshal.Cast<byte, short>(pcmBytes);
    var samples = new float[pcm.Length];
    for (var i = 0; i < pcm.Length; i++)
        samples[i] = pcm[i] / 32768f;

    return samples;
}

static string GarantirFfmpeg()
{
    var ffmpeg = EncontrarFfmpeg();
    if (ffmpeg is not null)
        return ffmpeg;

    if (OperatingSystem.IsWindows())
    {
        Console.WriteLine("ffmpeg não encontrado. Instalando via winget...");
        if (TentarInstalarFfmpeg())
        {
            ffmpeg = EncontrarFfmpeg();
            if (ffmpeg is not null)
            {
                Console.WriteLine($"ffmpeg instalado: {ffmpeg}");
                return ffmpeg;
            }
        }
    }

    throw new InvalidOperationException(
        "ffmpeg não encontrado e a instalação automática falhou.\n" +
        "Instale manualmente: winget install Gyan.FFmpeg\n" +
        "Depois reabra o terminal e tente novamente.");
}

static bool TentarInstalarFfmpeg()
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = "install Gyan.FFmpeg --accept-package-agreements --accept-source-agreements",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return false;

        process.WaitForExit();
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

static string? EncontrarFfmpeg()
{
    foreach (var dir in ObterDiretoriosPath())
    {
        var candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        if (File.Exists(candidate))
            return candidate;
    }

    if (OperatingSystem.IsWindows())
    {
        var winGetPackages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");

        if (Directory.Exists(winGetPackages))
        {
            foreach (var ffmpeg in Directory.EnumerateFiles(winGetPackages, "ffmpeg.exe", SearchOption.AllDirectories))
                return ffmpeg;
        }
    }

    return null;
}

static IEnumerable<string> ObterDiretoriosPath()
{
    var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var alvo in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH", alvo);
        if (string.IsNullOrWhiteSpace(pathEnv))
            continue;

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var normalizado = dir.Trim().Trim('"');
            if (vistos.Add(normalizado))
                yield return normalizado;
        }
    }
}

static List<(double OffsetSec, float[] Samples)> DividirPorSilencio(float[] audio, int sr = SampleRate)
{
    if (audio.Length == 0)
        return [];

    var frameSize = Math.Max(1, sr * FrameMs / 1000);
    var hop = Math.Max(1, frameSize / 2);
    var minSilenceFrames = Math.Max(1, MinSilenceMs / FrameMs);
    var minSpeechSamples = sr * MinSpeechMs / 1000;

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
                partes.Add((speechStart / (double)sr, chunk));
            }
            speechStart = i * hop;
        }

        silenceRun = 0;
    }

    if (audio.Length - speechStart >= minSpeechSamples)
    {
        var chunk = new float[audio.Length - speechStart];
        Array.Copy(audio, speechStart, chunk, 0, chunk.Length);
        partes.Add((speechStart / (double)sr, chunk));
    }

    if (partes.Count == 0)
        partes.Add((0, audio));

    return partes;
}

static (int MaxPartes, int Paralelismo, int ThreadsPorJob) CalcularLimitesParalelos(string dispositivo)
{
    var threads = Environment.ProcessorCount;

    if (dispositivo is "cuda" or "vulkan")
    {
        var paralelismo = Math.Max(1, Math.Min(2, threads / 4));
        var maxPartes = Math.Max(paralelismo, Math.Min(4, (int)Math.Ceiling(threads / 2.0)));
        var threadsPorJob = Math.Max(1, threads / paralelismo);
        return (maxPartes, paralelismo, threadsPorJob);
    }

    var cpuParalelismo = Math.Max(1, threads);
    var cpuThreadsPorJob = Math.Max(1, threads / cpuParalelismo);
    return (cpuParalelismo, cpuParalelismo, cpuThreadsPorJob);
}

static float[] CopiarChunks(List<float[]> chunks, int totalLen)
{
    var result = new float[totalLen];
    var pos = 0;
    foreach (var chunk in chunks)
    {
        Array.Copy(chunk, 0, result, pos, chunk.Length);
        pos += chunk.Length;
    }

    return result;
}

static List<(double OffsetSec, float[] Samples)> AgruparPartes(
    List<(double OffsetSec, float[] Samples)> trechos,
    int maxPartes)
{
    if (trechos.Count <= maxPartes || maxPartes <= 1)
        return trechos;

    var totalSamples = trechos.Sum(t => (long)t.Samples.Length);
    var targetSamples = totalSamples / (double)maxPartes;

    var agrupadas = new List<(double, float[])>();
    var bufferChunks = new List<float[]>();
    var bufferLen = 0;
    var offsetInicio = trechos[0].OffsetSec;

    foreach (var (offset, samples) in trechos)
    {
        if (bufferLen == 0)
            offsetInicio = offset;

        bufferChunks.Add(samples);
        bufferLen += samples.Length;

        var ultimaParte = agrupadas.Count >= maxPartes - 1;
        var atingiuAlvo = bufferLen >= targetSamples;

        if (!ultimaParte && atingiuAlvo)
        {
            agrupadas.Add((offsetInicio, CopiarChunks(bufferChunks, bufferLen)));
            bufferChunks.Clear();
            bufferLen = 0;
        }
    }

    if (bufferLen > 0)
        agrupadas.Add((offsetInicio, CopiarChunks(bufferChunks, bufferLen)));

    return agrupadas;
}

static string FormatarSegmento(TimeSpan inicio, TimeSpan fim, string texto)
{
    return $"[{inicio.TotalSeconds,6:F2}s -> {fim.TotalSeconds,6:F2}s] {texto.Trim()}";
}

static string FormatarTempo(TimeSpan tempo) =>
    tempo.TotalMinutes >= 1
        ? $"{(int)tempo.TotalMinutes}m {tempo.Seconds + tempo.Milliseconds / 1000.0:F2}s"
        : $"{tempo.TotalSeconds:F2}s";
