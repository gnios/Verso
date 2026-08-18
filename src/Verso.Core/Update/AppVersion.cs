namespace Verso.Core.Update;

public static class AppVersion
{
    public static Version Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new Version(0, 0, 0);

        var s = value.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            s = s[1..];

        var cut = s.IndexOfAny(['-', '+']);
        if (cut >= 0)
            s = s[..cut];

        if (!Version.TryParse(s, out var parsed))
            return new Version(0, 0, 0);

        return new Version(
            parsed.Major,
            parsed.Minor,
            parsed.Build < 0 ? 0 : parsed.Build);
    }

    public static bool IsNewer(string remoteTag, string localVersion) =>
        Parse(remoteTag) > Parse(localVersion);
}
