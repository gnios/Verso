namespace Verso.Core.Update;

public sealed class OverlayUpdateApplier
{
    public const string DataFolderName = "data";
    public const string StagingFolderName = "update-staging";
    private const int CopyAttempts = 30;
    private const int CopyRetryMilliseconds = 100;

    public OverlayApplyResult Apply(string stagingDirectory, string appDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);

        if (!Directory.Exists(stagingDirectory))
            return OverlayApplyResult.Aborted("staging ausente");

        if (!HasAppHost(stagingDirectory))
            return OverlayApplyResult.Aborted("pacote sem Verso.App");

        try
        {
            Directory.CreateDirectory(appDirectory);

            foreach (var file in Directory.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(stagingDirectory, file);
                if (ShouldSkipRelative(relative))
                    continue;

                var dest = Path.Combine(appDirectory, relative);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);
                CopyWithRetry(file, dest);
            }

            TryDeleteDirectory(Path.Combine(appDirectory, StagingFolderName));
            return OverlayApplyResult.Succeeded();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OverlayApplyResult.Aborted(ex.Message);
        }
    }

    public static bool ShouldSkipRelative(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.Equals(DataFolderName, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(DataFolderName + "/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Equals(StagingFolderName, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(StagingFolderName + "/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static bool HasAppHost(string directory) =>
        File.Exists(Path.Combine(directory, "Verso.App.exe"))
        || File.Exists(Path.Combine(directory, "Verso.App"));

    private static void CopyWithRetry(string source, string destination)
    {
        for (var attempt = 1; attempt <= CopyAttempts; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < CopyAttempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(CopyRetryMilliseconds);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
