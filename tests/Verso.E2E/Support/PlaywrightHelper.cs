namespace Verso.E2E.Support;

public static class PlaywrightHelper
{
    private static readonly object InstallLock = new();
    private static bool _installed;

    public static Task EnsureBrowsersInstalledAsync()
    {
        lock (InstallLock)
        {
            if (_installed)
            {
                return Task.CompletedTask;
            }

            var exit = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (exit != 0)
            {
                throw new InvalidOperationException($"playwright install chromium falhou (exit={exit}).");
            }

            _installed = true;
        }

        return Task.CompletedTask;
    }
}
