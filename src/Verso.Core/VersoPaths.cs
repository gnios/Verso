using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Verso.Core;

/// <summary>
/// Resolução central dos caminhos do Verso:
/// <list type="bullet">
/// <item><see cref="AppDirectory"/> — pasta do <c>Verso.App</c> (raiz do zip).</item>
/// <item><see cref="PayloadDirectory"/> — payload em <c>engine/</c> (Worker, wwwroot, runtimes, nativos);
/// em dev sem pasta <c>engine/</c>, cai no próprio <see cref="AppDirectory"/>.</item>
/// <item><see cref="DataRoot"/> — dados do usuário em <c>data/</c> ou em LocalAppData para MSIX.</item>
/// </list>
/// </summary>
/// <remarks>
/// Os consumidores usam estes caminhos como padrão, mas continuam aceitando um caminho
/// explícito (testes). Para E2E, defina <c>VERSO_DATA_ROOT</c>.
/// </remarks>
public static class VersoPaths
{
    public const string DataRootEnvironmentVariable = "VERSO_DATA_ROOT";

    public static bool IsPackagedWindowsApp { get; } = HasWindowsPackageIdentity();

    /// <summary>Nome da pasta de payload no layout de release.</summary>
    public const string PayloadFolderName = "engine";

    /// <summary>Diretório onde o executável do App roda (raiz do app portátil).</summary>
    public static string AppDirectory => AppContext.BaseDirectory;

    /// <summary>
    /// Diretório do payload (Worker, wwwroot, runtimes, DLLs nativas).
    /// Preferência: <c>&lt;appdir&gt;/engine</c> se existir; senão <see cref="AppDirectory"/> (dev).
    /// </summary>
    public static string PayloadDirectory
    {
        get
        {
            var nested = Path.Combine(AppDirectory, PayloadFolderName);
            return Directory.Exists(nested) ? nested : AppDirectory;
        }
    }

    /// <summary>
    /// Raiz dos dados de usuário. A variável <c>VERSO_DATA_ROOT</c> tem precedência.
    /// Criada sob demanda no primeiro acesso.
    /// </summary>
    public static string DataRoot
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
            var root = string.IsNullOrWhiteSpace(fromEnv)
                ? DefaultDataRoot()
                : Path.GetFullPath(fromEnv.Trim());
            Directory.CreateDirectory(root);
            return root;
        }
    }

    private static string DefaultDataRoot() => IsPackagedWindowsApp
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gnios", "Verso", "data")
        : Path.Combine(AppDirectory, "data");

    private static bool HasWindowsPackageIdentity()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        const int errorInsufficientBuffer = 122;
        uint length = 0;
        // Uma consulta sem buffer retorna o tamanho necessário quando há identidade de pacote.
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) == errorInsufficientBuffer;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    /// <summary>Caminho do banco SQLite: <c>&lt;dataroot&gt;/verso.db</c>.</summary>
    public static string DatabasePath => Path.Combine(DataRoot, "verso.db");

    /// <summary>Diretório de modelos whisper: <c>&lt;dataroot&gt;/models</c>.</summary>
    public static string ModelsDirectory => Path.Combine(DataRoot, "models");

    /// <summary>Diretório de mídia copiada: <c>&lt;dataroot&gt;/media</c>.</summary>
    public static string MediaDirectory => Path.Combine(DataRoot, "media");

    /// <summary>Diretório de logs em arquivo: <c>&lt;dataroot&gt;/logs</c>.</summary>
    public static string LogsDirectory => Path.Combine(DataRoot, "logs");
}
