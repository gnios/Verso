using System.Diagnostics;

namespace Verso.Core.Engine;

public sealed class FfmpegLocator
{
    private readonly Func<IEnumerable<string>> _getPathDirectories;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, IEnumerable<string>> _enumerateFiles;
    private readonly Func<bool> _tryInstallFfmpeg;
    private readonly bool _isWindows;

    public FfmpegLocator()
        : this(
            GetPathDirectoriesFromEnvironment,
            File.Exists,
            Directory.Exists,
            dir => Directory.EnumerateFiles(dir, "ffmpeg.exe", SearchOption.AllDirectories),
            TryInstallFfmpegDefault,
            OperatingSystem.IsWindows())
    {
    }

    internal FfmpegLocator(
        Func<IEnumerable<string>> getPathDirectories,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        Func<string, IEnumerable<string>> enumerateFiles,
        Func<bool> tryInstallFfmpeg,
        bool isWindows)
    {
        _getPathDirectories = getPathDirectories;
        _fileExists = fileExists;
        _directoryExists = directoryExists;
        _enumerateFiles = enumerateFiles;
        _tryInstallFfmpeg = tryInstallFfmpeg;
        _isWindows = isWindows;
    }

    public string EnsureFfmpeg()
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is not null)
            return ffmpeg;

        if (_isWindows && _tryInstallFfmpeg())
        {
            ffmpeg = FindFfmpeg();
            if (ffmpeg is not null)
                return ffmpeg;
        }

        throw new FfmpegNotFoundException();
    }

    public string? FindFfmpeg()
    {
        foreach (var dir in _getPathDirectories())
        {
            var candidate = Path.Combine(dir, _isWindows ? "ffmpeg.exe" : "ffmpeg");
            if (_fileExists(candidate))
                return candidate;
        }

        if (_isWindows)
        {
            var winGetPackages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Packages");

            if (_directoryExists(winGetPackages))
            {
                foreach (var ffmpeg in _enumerateFiles(winGetPackages))
                    return ffmpeg;
            }
        }

        return null;
    }

    internal static IEnumerable<string> GetPathDirectoriesFromEnvironment()
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        // User/Machine PATH: no Windows; em Unix só Process (User/Machine lançam PlatformNotSupportedException).
        var targets = OperatingSystem.IsWindows()
            ? new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }
            : new[] { EnvironmentVariableTarget.Process };

        foreach (var target in targets)
        {
            string? pathEnv;
            try
            {
                pathEnv = Environment.GetEnvironmentVariable("PATH", target);
            }
            catch (PlatformNotSupportedException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(pathEnv))
                continue;

            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var normalized = dir.Trim().Trim('"');
                if (seen.Add(normalized))
                    yield return normalized;
            }
        }
    }

    private static bool TryInstallFfmpegDefault()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "install Gyan.FFmpeg --accept-package-agreements --accept-source-agreements",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
