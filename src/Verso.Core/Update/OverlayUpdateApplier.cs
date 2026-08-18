namespace Verso.Core.Update;

public sealed class OverlayUpdateApplier
{
    public const string DataFolderName = "data";
    public const string StagingFolderName = "update-staging";

    public OverlayApplyResult Apply(string stagingDirectory, string appDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);

        if (!Directory.Exists(stagingDirectory))
            return OverlayApplyResult.Aborted("staging ausente");

        if (!HasAppHost(stagingDirectory))
            return OverlayApplyResult.Aborted("pacote sem Verso.App");

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
            File.Copy(file, dest, overwrite: true);
        }

        return OverlayApplyResult.Succeeded();
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
}
