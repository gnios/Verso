namespace Verso.Core;

/// <summary>
/// Public ingestion DSN for the desktop host and its transcription worker.
/// DSNs are client-side identifiers, not authentication secrets.
/// </summary>
public static class SentryConfiguration
{
    public const string DesktopDsn =
        "https://c234d7e8421fe7437d5293a681de7a99@o4512463990685696.ingest.us.sentry.io/4512464015720448";
}
