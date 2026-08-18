using System.Diagnostics;
using System.Reflection;

namespace Verso.Core.Update;

public interface IUpdateProcessLauncher
{
    bool TryLaunch(string appDirectory, string stagingDirectory, int currentPid, string launchPath);
}

public static class RunningAppVersion
{
    public static string Current
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            return string.IsNullOrWhiteSpace(informational)
                ? assembly.GetName().Version?.ToString() ?? "0.0.0"
                : informational;
        }
    }
}

public sealed class UpdateProcessLauncher : IUpdateProcessLauncher
{
    public static string UpdaterFileName { get; } =
        OperatingSystem.IsWindows() ? "Verso.Updater.exe" : "Verso.Updater";

    public static string AppHostFileName { get; } =
        OperatingSystem.IsWindows() ? "Verso.App.exe" : "Verso.App";

    public bool TryLaunch(string appDirectory, string stagingDirectory, int currentPid, string launchPath)
    {
        var updater = ResolveUpdater(appDirectory);
        if (updater is null)
            return false;

        var tempDir = Path.Combine(Path.GetTempPath(), "verso-updater-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempUpdater = Path.Combine(tempDir, Path.GetFileName(updater));
        File.Copy(updater, tempUpdater, overwrite: true);

        var info = new ProcessStartInfo
        {
            FileName = tempUpdater,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("--pid");
        info.ArgumentList.Add(currentPid.ToString());
        info.ArgumentList.Add("--app-dir");
        info.ArgumentList.Add(appDirectory);
        info.ArgumentList.Add("--staging");
        info.ArgumentList.Add(stagingDirectory);
        info.ArgumentList.Add("--launch");
        info.ArgumentList.Add(launchPath);

        using var process = Process.Start(info);
        return process is not null;
    }

    public static string? ResolveUpdater(string appDirectory)
    {
        var root = Path.Combine(appDirectory, UpdaterFileName);
        if (File.Exists(root))
            return root;

        var engine = Path.Combine(appDirectory, VersoPaths.PayloadFolderName, UpdaterFileName);
        return File.Exists(engine) ? engine : null;
    }
}

public sealed class UpdateSession
{
    private readonly UpdateCoordinator _coordinator;
    private readonly IUpdateProcessLauncher _launcher;
    private readonly Func<int> _currentPid;
    private readonly Action _requestExit;
    private readonly Func<string> _appDirectory;
    private readonly Func<string> _launchPath;

    public UpdateSession(
        UpdateCoordinator coordinator,
        IUpdateProcessLauncher launcher,
        Func<int>? currentPid = null,
        Action? requestExit = null,
        Func<string>? appDirectory = null,
        Func<string>? launchPath = null)
    {
        _coordinator = coordinator;
        _launcher = launcher;
        _currentPid = currentPid ?? (() => Environment.ProcessId);
        _requestExit = requestExit ?? (() => Environment.Exit(0));
        _appDirectory = appDirectory ?? (() => VersoPaths.AppDirectory);
        _launchPath = launchPath ?? (() => Path.Combine(VersoPaths.AppDirectory, UpdateProcessLauncher.AppHostFileName));
    }

    public bool TryApplyPendingAndRequestExit()
    {
        if (!_coordinator.HasPendingApply())
            return false;

        var launched = _launcher.TryLaunch(
            _appDirectory(),
            _coordinator.PayloadDirectory,
            _currentPid(),
            _launchPath());
        if (!launched)
            return false;

        _requestExit();
        return true;
    }

    public async Task CheckInBackgroundAsync(CancellationToken cancellationToken = default)
    {
        var result = await _coordinator.CheckAndPrepareAsync(cancellationToken).ConfigureAwait(false);
        if (result.ApplyImmediately)
            TryApplyPendingAndRequestExit();
    }
}
