using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Verso.Core;

/// <summary>
/// Detecção de memória física total do computador. Usada por
/// <see cref="Engine.ModelRecommender"/> para sugerir um modelo Whisper adequado
/// ao hardware. Não unit-testado (depende do ambiente); a lógica de decisão
/// testável vive em <c>ModelRecommender.Recommend</c>.
/// </summary>
public static class SystemMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    /// <summary>Memória física total em bytes, ou 0 se a chamada falhar.</summary>
    public static long TotalPhysicalMemoryBytes
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return TryWindowsPhysicalMemoryBytes();

            if (OperatingSystem.IsLinux())
                return TryLinuxPhysicalMemoryBytes();

            if (OperatingSystem.IsMacOS())
                return TryMacPhysicalMemoryBytes();

            return FallbackAvailableMemoryBytes();
        }
    }

    /// <summary>Memória física total em GB (arredondada para baixo).</summary>
    public static long TotalPhysicalMemoryGb
    {
        get
        {
            var bytes = TotalPhysicalMemoryBytes;
            return bytes <= 0 ? 0 : bytes / (1024L * 1024L * 1024L);
        }
    }

    [SupportedOSPlatform("windows")]
    private static long TryWindowsPhysicalMemoryBytes()
    {
        var mem = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref mem) ? (long)mem.ullTotalPhys : 0;
    }

    private static long TryLinuxPhysicalMemoryBytes()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (!line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    continue;

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1], out var kib))
                    return kib * 1024L;
            }
        }
        catch
        {
            // fallback abaixo
        }

        return FallbackAvailableMemoryBytes();
    }

    private static long TryMacPhysicalMemoryBytes()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sysctl",
                Arguments = "-n hw.memsize",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
                return FallbackAvailableMemoryBytes();

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);
            if (long.TryParse(output, out var bytes) && bytes > 0)
                return bytes;
        }
        catch
        {
            // fallback abaixo
        }

        return FallbackAvailableMemoryBytes();
    }

    private static long FallbackAvailableMemoryBytes()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            return info.TotalAvailableMemoryBytes > 0 ? info.TotalAvailableMemoryBytes : 0;
        }
        catch
        {
            return 0;
        }
    }
}
