namespace Verso.Core.Update;

public sealed class UpdateApplyCommand
{
    public int Pid { get; init; }
    public string AppDirectory { get; init; } = "";
    public string StagingDirectory { get; init; } = "";
    public string LaunchPath { get; init; } = "";

    public static UpdateApplyCommand? TryParse(string[] args)
    {
        string? pid = null, app = null, staging = null, launch = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--pid":
                    pid = args[++i];
                    break;
                case "--app-dir":
                    app = args[++i];
                    break;
                case "--staging":
                    staging = args[++i];
                    break;
                case "--launch":
                    launch = args[++i];
                    break;
            }
        }

        if (!int.TryParse(pid, out var pidValue) || pidValue < 0
            || string.IsNullOrWhiteSpace(app)
            || string.IsNullOrWhiteSpace(staging)
            || string.IsNullOrWhiteSpace(launch))
        {
            return null;
        }

        return new UpdateApplyCommand
        {
            Pid = pidValue,
            AppDirectory = app,
            StagingDirectory = staging,
            LaunchPath = launch
        };
    }

    public int Execute(
        OverlayUpdateApplier applier,
        Func<int, bool> isProcessRunning,
        Func<string, string, bool> startProcess,
        int pollMilliseconds = 50,
        int timeoutMilliseconds = 120_000,
        int settleMilliseconds = 500)
    {
        var waited = 0;
        while (isProcessRunning(Pid) && waited < timeoutMilliseconds)
        {
            Thread.Sleep(pollMilliseconds);
            waited += pollMilliseconds;
        }

        if (isProcessRunning(Pid))
            return 3;

        if (settleMilliseconds > 0)
            Thread.Sleep(settleMilliseconds);

        OverlayApplyResult result;
        try
        {
            result = applier.Apply(StagingDirectory, AppDirectory);
        }
        catch (Exception ex)
        {
            result = OverlayApplyResult.Aborted(ex.Message);
        }

        var launched = startProcess(LaunchPath, AppDirectory);
        if (!result.Success)
            return launched ? 1 : 4;

        return launched ? 0 : 2;
    }
}
