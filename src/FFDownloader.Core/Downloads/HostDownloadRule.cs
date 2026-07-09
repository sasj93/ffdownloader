namespace FFDownloader.Core.Downloads;

public sealed record HostDownloadRule(
    string Host,
    int MaxConnectionsPerFile,
    long MinMultiConnectionSizeBytes,
    bool AllowMultiConnection);

public static class HostDownloadRules
{
    private static readonly HostDownloadRule DefaultRule = new("*", 8, 16L * 1024 * 1024, true);

    private static readonly HostDownloadRule[] Rules =
    [
        new("fuckingfast.co", 6, 8L * 1024 * 1024, true),
        new("datanodes.to", 6, 8L * 1024 * 1024, true),
        new("mediafire.com", 4, 8L * 1024 * 1024, true)
    ];

    public static HostDownloadRule GetForHost(string host)
    {
        return Rules.FirstOrDefault(rule => host.EndsWith(rule.Host, StringComparison.OrdinalIgnoreCase))
            ?? DefaultRule;
    }
}
