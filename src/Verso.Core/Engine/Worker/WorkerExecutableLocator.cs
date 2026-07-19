namespace Verso.Core.Engine.Worker;

public interface IWorkerExecutableLocator
{
    string Resolve();
}

/// <summary>
/// Resolve o caminho absoluto do Verso.Worker em <see cref="VersoPaths.PayloadDirectory"/>
/// (release: <c>engine/</c>; dev: ao lado do App).
/// Windows: <c>Verso.Worker.exe</c>; Linux/macOS: <c>Verso.Worker</c>.
/// </summary>
public sealed class WorkerExecutableLocator : IWorkerExecutableLocator
{
    /// <summary>Nome do apphost do worker na plataforma atual.</summary>
    public static string WorkerFileName { get; } =
        OperatingSystem.IsWindows() ? "Verso.Worker.exe" : "Verso.Worker";

    private readonly Func<string> _baseDirectory;
    private readonly Func<string, bool> _fileExists;
    private readonly string _workerFileName;

    public WorkerExecutableLocator()
        : this(() => VersoPaths.PayloadDirectory, File.Exists, WorkerFileName)
    {
    }

    internal WorkerExecutableLocator(Func<string> baseDirectory, Func<string, bool> fileExists)
        : this(baseDirectory, fileExists, WorkerFileName)
    {
    }

    internal WorkerExecutableLocator(
        Func<string> baseDirectory,
        Func<string, bool> fileExists,
        string workerFileName)
    {
        _baseDirectory = baseDirectory;
        _fileExists = fileExists;
        _workerFileName = workerFileName;
    }

    public string Resolve()
    {
        var candidate = Path.Combine(_baseDirectory(), _workerFileName);
        if (_fileExists(candidate))
            return candidate;

        throw new FileNotFoundException(
            $"{_workerFileName} não encontrado em '{candidate}'. Verifique se o build/empacotamento " +
            $"colocou o worker em '{VersoPaths.PayloadFolderName}/' (release) ou ao lado do Verso.App (dev).",
            candidate);
    }
}
