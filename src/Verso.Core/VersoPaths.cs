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
/// </remarks>
public static class VersoPaths
{
    /// <summary>Diretório onde o executável roda (raiz do app portátil).</summary>
    public static string AppDirectory => AppContext.BaseDirectory;

    /// <summary>
    /// Raiz dos dados de usuário: <c>&lt;appdir&gt;/data</c>. Criada sob demanda no
    /// primeiro acesso. Tudo de persistente fica aqui dentro.
    /// </summary>
    public static string DataRoot
    {
        get
        {
            var root = Path.Combine(AppDirectory, "data");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    /// <summary>Caminho do banco SQLite: <c>&lt;appdir&gt;/data/verso.db</c>.</summary>
    public static string DatabasePath => Path.Combine(DataRoot, "verso.db");

    /// <summary>Diretório de modelos whisper: <c>&lt;appdir&gt;/data/models</c>.</summary>
    public static string ModelsDirectory => Path.Combine(DataRoot, "models");

    /// <summary>Diretório de mídia copiada: <c>&lt;appdir&gt;/data/media</c>.</summary>
    public static string MediaDirectory => Path.Combine(DataRoot, "media");

    /// <summary>Diretório de logs em arquivo: <c>&lt;appdir&gt;/data/logs</c>.</summary>
    public static string LogsDirectory => Path.Combine(DataRoot, "logs");
}