using System.Text.RegularExpressions;

namespace FFDownloader.Core.Torrents;

public static partial class TorrentSourceParser
{
    public static IReadOnlyList<string> ParseMagnetLinks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return MagnetLinkRegex()
            .Matches(text)
            .Select(match => match.Value.TrimEnd('.', ',', ';', ')', ']', '}', '>', '"', '\''))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsTorrentFilePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"magnet:\?[^\s<>'""\])}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MagnetLinkRegex();
}
