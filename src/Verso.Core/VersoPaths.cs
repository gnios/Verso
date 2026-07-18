using System;
using System.IO;

namespace Verso.Core;

/// <summary>
/// Resolução central dos caminhos de dados do Verso. O app é **portátil**: todos os
/// arquivos de usuário (banco SQLite, modelos whisper, mídia copiada, logs) ficam em
/// uma pasta <c>data/</c> ao lado do executável (<see cref="AppContext.BaseDirectory"/>),
/// e não em <c>%AppData%</c>. Assim, mover/copiar a pasta do app leva junto todos os
/// dados — sem dependência de um local fixo do sistema.
/// </summary>
/// <remarks>
/// Os consumidores (<see cref="Data.DbBootstrapper"/>, <see cref="Services.MediaStorageService"/>,
/// <see cref="Engine.WhisperTranscriptionEngine"/>, <see cref="Logging.FileLoggerOptions"/>)
/// usam estes caminhos como padrão, mas continuam aceitando um caminho explícito (usado
/// pelos testes), de modo que a portabilidade só vale quando nada é sobrescrito.
/// <para>
/// Para E2E, defina <c>VERSO_DATA_ROOT</c> apontando para um diretório isolado
/// (substitui <c>&lt;appdir&gt;/data</c>).
/// </para>
/// </remarks>
public static class VersoPaths
{
    public const string DataRootEnvironmentVariable = "VERSO_DATA_ROOT";

    /// <summary>Diretório onde o executável roda (raiz do app portátil).</summary>
    public static string AppDirectory => AppContext.BaseDirectory;

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
