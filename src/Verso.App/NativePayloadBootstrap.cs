using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Verso.Core;
using Whisper.net.LibraryLoader;

namespace Verso.App;

/// <summary>
/// Configura PATH / DllDirectory e o probe do whisper.net para o payload em
/// <see cref="VersoPaths.PayloadDirectory"/> (<c>engine/</c> no release).
/// Deve rodar antes de qualquer carga Photino/SQLite/whisper.
/// </summary>
internal static class NativePayloadBootstrap
{
    public static void Apply()
    {
        var payload = Path.GetFullPath(VersoPaths.PayloadDirectory);

        // Whisper.net usa GetDirectoryName(LibraryPath) como raiz de busca de runtimes/.
        RuntimeOptions.LibraryPath = Path.Combine(payload, "verso.payload");

        PrependPath(payload);

        if (OperatingSystem.IsWindows())
            SetDllDirectory(payload);
    }

    private static void PrependPath(string directory)
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(p => string.Equals(p, directory, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + current);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string lpPathName);
}
