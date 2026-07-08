using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Transcriba.Core;

/// <summary>
/// Detecção de memória física total do computador (Windows). Usada por
/// <see cref="Engine.ModelRecommender"/> para sugerir um modelo Whisper adequado
/// ao hardware. Não unit-testado (depende do ambiente); a lógica de decisão
/// testável vive em <c>ModelRecommender.Recommend</c>.
/// </summary>
[SupportedOSPlatform("windows")]
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
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    /// <summary>Memória física total em bytes, ou 0 se a chamada falhar.</summary>
    public static long TotalPhysicalMemoryBytes
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return 0;
            }

            var mem = new MemoryStatusEx { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref mem) ? (long)mem.ullTotalPhys : 0;
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
}