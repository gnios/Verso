using System;
using System.IO;

namespace Verso.Core;

/// <summary>
/// Resolução central dos caminhos do Verso. O app é **portátil**:
/// <list type="bullet">
/// <item><see cref="AppDirectory"/> — pasta do <c>Verso.App</c> (raiz do zip).</item>
/// <item><see cref="PayloadDirectory"/> — payload em <c>engine/</c> (Worker, wwwroot, runtimes, nativos);
/// em dev sem pasta <c>engine/</c>, cai no próprio <see cref="AppDirectory"/>.</item>
/// <item><see cref="DataRoot"/> — dados do usuário em <c>data/</c> ao lado do exe (não em %AppData%).</item>
/// </list>
/// </summary>
/// <remarks>
/// Os consumidores usam estes caminhos como padrão, mas continuam aceitando um caminho
/// explícito (testes). Para E2E, defina <c>VERSO_DATA_ROOT</c>.
/// </remarks>
public static class VersoPaths
{
    public const string DataRootEnvironmentVariable = "VERSO_DATA_ROOT";

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
    /// Raiz dos dados de usuário: <c>VERSO_DATA_ROOT</c> ou <c>&lt;appdir&gt;/data</c>.
    /// Criada sob demanda no primeiro acesso.
    /// </summary>
    public static string DataRoot
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
            var root = string.IsNullOrWhiteSpace(fromEnv)
                ? Path.Combine(AppDirectory, "data")
                : Path.GetFullPath(fromEnv.Trim());
            Directory.CreateDirectory(root);
            return root;
        }
    }

    /// <summary>Caminho do banco SQLite: <c>&lt;dataroot&gt;/verso.db</c>.</summary>
    public static string DatabasePath => Path.Combine(DataRoot, "verso.db");

    /// <summary>Diretório de modelos whisper: <c>&lt;dataroot&gt;/models</c>.</summary>
    public static string ModelsDirectory => Path.Combine(DataRoot, "models");

    /// <summary>Diretório de mídia copiada: <c>&lt;dataroot&gt;/media</c>.</summary>
    public static string MediaDirectory => Path.Combine(DataRoot, "media");

    /// <summary>Diretório de logs em arquivo: <c>&lt;dataroot&gt;/logs</c>.</summary>
    public static string LogsDirectory => Path.Combine(DataRoot, "logs");
}
