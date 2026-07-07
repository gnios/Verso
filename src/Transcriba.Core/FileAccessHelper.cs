namespace Transcriba.Core;

internal static class FileAccessHelper
{
    public static void RunWithRetry(Action action, int maxAttempts = 5, int delayMs = 200)
    {
        IOException? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException ex) when (attempt < maxAttempts)
            {
                lastError = ex;
                Thread.Sleep(delayMs * attempt);
            }
            catch (IOException ex)
            {
                throw new IOException("Falha após várias tentativas de acesso ao arquivo.", ex);
            }
        }

        if (lastError is not null)
        {
            throw new IOException("Falha após várias tentativas de acesso ao arquivo.", lastError);
        }
    }

    public static async Task RunWithRetryAsync(
        Func<Task> action,
        int maxAttempts = 5,
        int delayMs = 200,
        CancellationToken cancellationToken = default)
    {
        IOException? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await action();
                return;
            }
            catch (IOException ex) when (attempt < maxAttempts)
            {
                lastError = ex;
                await Task.Delay(delayMs * attempt, cancellationToken);
            }
            catch (IOException ex)
            {
                throw new IOException("Falha após várias tentativas de acesso ao arquivo.", ex);
            }
        }

        if (lastError is not null)
        {
            throw new IOException("Falha após várias tentativas de acesso ao arquivo.", lastError);
        }
    }
}
