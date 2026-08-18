using System.Diagnostics;
using Verso.Core.Update;

namespace Verso.Updater;

internal static class Program
{
    private static int Main(string[] args)
    {
        var command = UpdateApplyCommand.TryParse(args);
        if (command is null)
            return 2;

        return command.Execute(
            new OverlayUpdateApplier(),
            IsRunning,
            StartApp);
    }

    private static bool IsRunning(int pid)
    {
        if (pid <= 0)
            return false;

        try
        {
            var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool StartApp(string launchPath, string workingDirectory)
    {
        if (!File.Exists(launchPath))
            return false;

        var info = new ProcessStartInfo
        {
            FileName = launchPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };
        using var started = Process.Start(info);
        return started is not null;
    }
}
