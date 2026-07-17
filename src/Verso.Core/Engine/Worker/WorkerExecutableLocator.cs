namespace Verso.Core.Engine.Worker;

public interface IWorkerExecutableLocator
{
    string Resolve();
}

/// <summary>
/// Resolve o caminho absoluto de <c>Verso.Worker.exe</c>, sempre esperado como sibling do
/// executável do <c>Verso.App</c> (mesmo diretório, garantido por build target + empacotamento).
/// </summary>
public sealed class WorkerExecutableLocator : IWorkerExecutableLocator
{
    private const string WorkerExecutableName = "Verso.Worker.exe";

    private readonly Func<string> _baseDirectory;
    private readonly Func<string, bool> _fileExists;

    public WorkerExecutableLocator()
        : this(() => VersoPaths.AppDirectory, File.Exists)
    {
    }

    internal WorkerExecutableLocator(Func<string> baseDirectory, Func<string, bool> fileExists)
    {
        _baseDirectory = baseDirectory;
        _fileExists = fileExists;
    }

    public string Resolve()
    {
        var candidate = Path.Combine(_baseDirectory(), WorkerExecutableName);
        if (_fileExists(candidate))
            return candidate;

        throw new FileNotFoundException(
            $"Verso.Worker.exe não encontrado em '{candidate}'. Verifique se o build/empacotamento " +
            "copiou o worker para o mesmo diretório do Verso.App.",
            candidate);
    }
}
