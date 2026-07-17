using System.Diagnostics;

namespace Verso.Core.Engine.Worker;

/// <summary>
/// Abstração fina sobre um processo worker com stdio redirecionado, para permitir testar
/// <see cref="WorkerProcessTranscriptionEngine"/> sem spawnar um processo real.
/// </summary>
public interface IWorkerProcess : IAsyncDisposable
{
    TextWriter StandardInput { get; }
    TextReader StandardOutput { get; }
    Task<int> WaitForExitAsync(CancellationToken cancellationToken);
    void Kill();
}

public interface IWorkerProcessFactory
{
    IWorkerProcess Start(string exePath);
}

/// <summary>Implementação real de <see cref="IWorkerProcess"/> sobre <see cref="Process"/>.</summary>
public sealed class WorkerProcess : IWorkerProcess
{
    private readonly Process _process;

    internal WorkerProcess(Process process)
    {
        _process = process;
    }

    public TextWriter StandardInput => _process.StandardInput;
    public TextReader StandardOutput => _process.StandardOutput;

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken);
        return _process.ExitCode;
    }

    public void Kill()
    {
        if (!_process.HasExited)
            _process.Kill(entireProcessTree: true);
    }

    public ValueTask DisposeAsync()
    {
        _process.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Implementação real de <see cref="IWorkerProcessFactory"/> sobre <see cref="Process.Start(ProcessStartInfo)"/>.</summary>
public sealed class WorkerProcessFactory : IWorkerProcessFactory
{
    public IWorkerProcess Start(string exePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Não foi possível iniciar o processo worker: {exePath}");

        return new WorkerProcess(process);
    }
}
