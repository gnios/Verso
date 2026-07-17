using System.Text.Json;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Engine.Worker;

namespace Verso.Tests.Engine;

public class WorkerProcessTranscriptionEngineTests
{
    [Fact]
    public async Task TranscribeAsync_HappyPath_ReturnsResult_AndForwardsProgressInOrder()
    {
        var resultMessage = new WorkerResultMessage(new TranscriptionResult(
        [
            new TranscriptionSegmentResult(0, 1, "segmento ok"),
        ]));

        var process = FakeWorkerProcess.ThatExitsNaturally(
            exitCode: 0,
            outputLines:
            [
                Serialize(new WorkerProgressMessage("loading", null, null)),
                Serialize(new WorkerProgressMessage("transcribing", 1, 2)),
                Serialize(resultMessage),
            ]);

        var engine = CreateEngine(process, out var factory);
        var progress = new RecordingProgress();

        var result = await engine.TranscribeAsync(CreateRequest(), progress, CancellationToken.None);

        Assert.Equal("worker-exe-path", factory.RequestedExePath);
        Assert.Single(result.Segments);
        Assert.Equal("segmento ok", result.Segments[0].Text);

        Assert.Equal(2, progress.Reports.Count);
        Assert.Equal("loading", progress.Reports[0].Stage);
        Assert.Equal("transcribing", progress.Reports[1].Stage);
        Assert.Equal(1, progress.Reports[1].PartIndex);
        Assert.Equal(2, progress.Reports[1].TotalParts);

        Assert.Contains("\"type\":\"job\"", process.Input.ToString());
    }

    [Fact]
    public async Task TranscribeAsync_WhenWorkerReportsError_ThrowsWithWorkerMessage()
    {
        var process = FakeWorkerProcess.ThatExitsNaturally(
            exitCode: 1,
            outputLines: [Serialize(new WorkerErrorMessage("falha simulada no worker"))]);

        var engine = CreateEngine(process, out _);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.TranscribeAsync(CreateRequest(), progress: null, CancellationToken.None));

        Assert.Equal("falha simulada no worker", ex.Message);
    }

    [Fact]
    public async Task TranscribeAsync_WhenProcessExitsWithoutResultOrError_ThrowsWithoutCrashingParent()
    {
        // Simula um "crash" do worker: encerra com exit code != 0 sem nunca enviar result/error.
        var process = FakeWorkerProcess.ThatExitsNaturally(exitCode: -1073741819, outputLines: []);

        var engine = CreateEngine(process, out _);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.TranscribeAsync(CreateRequest(), progress: null, CancellationToken.None));

        Assert.Contains("-1073741819", ex.Message);
    }

    [Fact]
    public async Task TranscribeAsync_WhenCancelled_WritesCancelMessage_ThenKillsAfterTimeout()
    {
        // Nunca produz saída nem sai sozinho: força o caminho de timeout + Kill().
        var process = FakeWorkerProcess.ThatNeverExitsOnItsOwn();

        var engine = CreateEngine(
            process,
            out _,
            gracefulShutdownTimeout: TimeSpan.FromMilliseconds(30));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            engine.TranscribeAsync(CreateRequest(), progress: null, cts.Token));

        Assert.NotNull(ex);
        Assert.Contains("\"type\":\"cancel\"", process.Input.ToString());
        Assert.True(process.Killed);
    }

    private static WorkerProcessTranscriptionEngine CreateEngine(
        IWorkerProcess process,
        out FakeWorkerProcessFactory factory,
        TimeSpan? gracefulShutdownTimeout = null)
    {
        factory = new FakeWorkerProcessFactory(process);
        var locator = new FakeWorkerExecutableLocator("worker-exe-path");

        return gracefulShutdownTimeout is { } timeout
            ? new WorkerProcessTranscriptionEngine(locator, factory, timeout)
            : new WorkerProcessTranscriptionEngine(locator, factory);
    }

    private static TranscriptionJobRequest CreateRequest() =>
        new(Guid.NewGuid(), "sample.wav", "pt", ModelQuality.Standard, ExecutionDevice.Cpu);

    private static string Serialize(WorkerMessage message) =>
        JsonSerializer.Serialize<WorkerMessage>(message, WorkerProtocol.JsonOptions);

    private sealed class RecordingProgress : IProgress<EngineProgress>
    {
        public List<EngineProgress> Reports { get; } = [];
        public void Report(EngineProgress value) => Reports.Add(value);
    }

    private sealed class FakeWorkerExecutableLocator(string path) : IWorkerExecutableLocator
    {
        public string Resolve() => path;
    }

    private sealed class FakeWorkerProcessFactory(IWorkerProcess process) : IWorkerProcessFactory
    {
        public string? RequestedExePath { get; private set; }

        public IWorkerProcess Start(string exePath)
        {
            RequestedExePath = exePath;
            return process;
        }
    }

    /// <summary>
    /// Fake de <see cref="IWorkerProcess"/> controlável: expõe o que foi escrito em stdin via
    /// <see cref="Input"/>, e permite scriptar linhas de stdout que "chegam" antes de o processo
    /// encerrar (ou nunca encerrar sozinho, para exercitar o caminho de cancelamento/kill).
    /// </summary>
    private sealed class FakeWorkerProcess : IWorkerProcess
    {
        private readonly ScriptedTextReader _output;
        private readonly TaskCompletionSource<int> _exitTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StringWriter Input { get; } = new();
        public bool Killed { get; private set; }

        private FakeWorkerProcess(ScriptedTextReader output) => _output = output;

        public static FakeWorkerProcess ThatExitsNaturally(int exitCode, IEnumerable<string> outputLines)
        {
            var output = new ScriptedTextReader(outputLines);
            output.SignalClosed(); // não há mais linhas: EOF assim que a fila esvaziar
            var process = new FakeWorkerProcess(output);
            process._exitTcs.TrySetResult(exitCode);
            return process;
        }

        public static FakeWorkerProcess ThatNeverExitsOnItsOwn() =>
            new(new ScriptedTextReader([]));

        public TextWriter StandardInput => Input;
        public TextReader StandardOutput => _output;

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) =>
            _exitTcs.Task.WaitAsync(cancellationToken);

        public void Kill()
        {
            Killed = true;
            _output.SignalClosed();
            _exitTcs.TrySetResult(-1);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// <see cref="TextReader"/> em memória cujas linhas são pré-definidas; se a fila esvaziar antes
    /// de <see cref="SignalClosed"/> ser chamado, a leitura fica pendurada (simulando um processo
    /// ainda em execução), como um pipe real de stdout.
    /// </summary>
    private sealed class ScriptedTextReader(IEnumerable<string> lines) : TextReader
    {
        private readonly Queue<string> _lines = new(lines);
        private readonly TaskCompletionSource _closedSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SignalClosed() => _closedSignal.TrySetResult();

        public override async Task<string?> ReadLineAsync()
        {
            if (_lines.Count > 0)
                return _lines.Dequeue();

            await _closedSignal.Task;
            return null;
        }

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            await ReadLineAsync();
    }
}
