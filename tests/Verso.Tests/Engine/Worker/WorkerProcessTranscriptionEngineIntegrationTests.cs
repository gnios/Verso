using NAudio.Wave;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Engine.Worker;

namespace Verso.Tests.Engine.Worker;

/// <summary>
/// Testes de integração ponta a ponta (Fase 2/transcricao-cpu-responsiva) de
/// <see cref="WorkerProcessTranscriptionEngine"/> contra o <c>Verso.Worker</c> REAL construído
/// (não os fakes de <c>IWorkerProcess</c>/<c>IWorkerExecutableLocator</c> usados em
/// <c>WorkerProcessTranscriptionEngineTests</c>): spawna o executável de verdade, fala o protocolo
/// NDJSON via stdio de verdade e roda o motor Whisper real dentro do processo filho.
///
/// Tagged "Integration" no nome do arquivo (convenção do repo): excluído do gate padrão
/// (`dotnet test --filter "FullyQualifiedName!~Integration"`) porque o primeiro teste baixa um
/// modelo GGML real (~75 MB, modelo "tiny") via rede caso ainda não esteja em cache no diretório
/// de output do Verso.Worker — ver <see cref="TranscribeAsync_WithRealWorkerProcess_ReturnsNonEmptySegments"/>.
/// Execução manual: `dotnet test Verso.sln --no-build --filter "FullyQualifiedName~Integration"`.
/// </summary>
public class WorkerProcessTranscriptionEngineIntegrationTests : IDisposable
{
    [Fact]
    public async Task TranscribeAsync_WithRealWorkerProcess_ReturnsNonEmptySegments()
    {
        var binDirectory = ResolveWorkerBinDirectory();
        EnsureWorkerExeAlias(binDirectory);

        var engine = new WorkerProcessTranscriptionEngine(
            new WorkerExecutableLocator(() => binDirectory, File.Exists),
            new WorkerProcessFactory());

        var wavPath = CreateTempWav(seconds: 2, frequencyHz: 440);
        var request = new TranscriptionJobRequest(
            Guid.NewGuid(),
            wavPath,
            Language: "pt",
            Quality: ModelQuality.Tiny,
            Device: ExecutionDevice.Cpu);

        // Timeout generoso: cobre um eventual download a frio do modelo "tiny" pelo próprio
        // Verso.Worker (ModelManager real, sem fake) além do tempo de transcrição em si.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var result = await engine.TranscribeAsync(request, progress: null, cts.Token);

        Assert.NotEmpty(result.Segments);
        Assert.All(result.Segments, segment => Assert.False(string.IsNullOrWhiteSpace(segment.Text)));
    }

    [Fact]
    public async Task TranscribeAsync_WhenWorkerReportsMissingMediaFile_ThrowsInvalidOperationExceptionWithoutCrashingCaller()
    {
        var binDirectory = ResolveWorkerBinDirectory();
        EnsureWorkerExeAlias(binDirectory);

        var engine = new WorkerProcessTranscriptionEngine(
            new WorkerExecutableLocator(() => binDirectory, File.Exists),
            new WorkerProcessFactory());

        var missingMediaPath = Path.Combine(Path.GetTempPath(), $"verso-integration-missing-{Guid.NewGuid():N}.wav");
        var request = new TranscriptionJobRequest(
            Guid.NewGuid(),
            missingMediaPath,
            Language: "pt",
            Quality: ModelQuality.Tiny,
            Device: ExecutionDevice.Cpu);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        // O worker real trata a falha internamente (WorkerHost captura a exceção do motor e
        // devolve uma mensagem "error" + exit code 1); o processo pai (este teste) só deve ver
        // uma InvalidOperationException catchável — nunca uma exceção não tratada/crash aqui.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.TranscribeAsync(request, progress: null, cts.Token));

        Assert.Contains(missingMediaPath, ex.Message);
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private readonly List<string> _tempFiles = [];

    private string CreateTempWav(double seconds, float frequencyHz)
    {
        var path = Path.Combine(Path.GetTempPath(), $"verso-integration-{Guid.NewGuid():N}.wav");
        _tempFiles.Add(path);

        var sampleRate = AudioLoader.SampleRate;
        var sampleCount = (int)(seconds * sampleRate);
        var format = new WaveFormat(sampleRate, 16, 1);

        using var writer = new WaveFileWriter(path, format);
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(MathF.Sin(2 * MathF.PI * frequencyHz * i / sampleRate) * short.MaxValue * 0.5f);
            writer.WriteSample(sample / (float)short.MaxValue);
        }

        return path;
    }

    /// <summary>
    /// Localiza o diretório de output já construído do <c>Verso.Worker</c>
    /// (<c>src/Verso.Worker/bin/{Debug,Release}/net10.0</c>), subindo a partir do diretório do
    /// teste até achar a raiz do repositório (marcada por <c>Verso.sln</c>).
    /// </summary>
    private static string ResolveWorkerBinDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Verso.sln")))
            directory = directory.Parent;

        if (directory is null)
        {
            throw new InvalidOperationException(
                "Não foi possível localizar a raiz do repositório (Verso.sln) a partir do diretório de testes.");
        }

        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var candidate = Path.Combine(directory.FullName, "src", "Verso.Worker", "bin", configuration, "net10.0");
            var hasExe = File.Exists(Path.Combine(candidate, "Verso.Worker.exe"));
            var hasAppHost = File.Exists(Path.Combine(candidate, "Verso.Worker"));
            if (hasExe || hasAppHost)
                return candidate;
        }

        throw new InvalidOperationException(
            "Build do Verso.Worker não encontrado em src/Verso.Worker/bin/{Debug,Release}/net10.0. " +
            "Execute 'dotnet build Verso.sln' antes deste teste de integração.");
    }

    /// <summary>
    /// Adaptação apenas para ambientes de desenvolvimento sem sufixo ".exe" nativo (ex.:
    /// macOS/arm64, onde o apphost gerado se chama simplesmente "Verso.Worker"): cria, no MESMO
    /// diretório de output, uma cópia do apphost com o nome exato que
    /// <see cref="WorkerExecutableLocator"/> (produção, inalterado) espera. Preserva os arquivos
    /// irmãos (Verso.Worker.dll, runtimeconfig.json, runtimes/) exigidos pelo apphost para
    /// resolver o runtime gerenciado. Em CI/Windows, "Verso.Worker.exe" já existe e este método é
    /// um no-op.
    /// </summary>
    private static void EnsureWorkerExeAlias(string binDirectory)
    {
        var exePath = Path.Combine(binDirectory, "Verso.Worker.exe");
        if (File.Exists(exePath))
            return;

        var appHostPath = Path.Combine(binDirectory, "Verso.Worker");
        if (!File.Exists(appHostPath))
        {
            throw new InvalidOperationException(
                $"Nem 'Verso.Worker.exe' nem 'Verso.Worker' encontrados em '{binDirectory}'.");
        }

        File.Copy(appHostPath, exePath, overwrite: true);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                exePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }
}
