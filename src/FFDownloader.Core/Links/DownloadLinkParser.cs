using System.Text.RegularExpressions;

namespace FFDownloader.Core.Links;

public static partial class DownloadLinkParser
{
    /// <summary>
    /// Hosts multiup.io mirrors that are handled generically by <c>GenericBrowserMirrorResolver</c>
    /// (a hidden/interactive WebView2 click-and-capture flow) rather than a dedicated per-host parser.
    /// Excludes hosts that need a fundamentally different protocol: mega.nz (own crypto scheme), cloud
    /// storage needing OAuth (drive/dropbox/onedrive), and ftp/ftp2 (not HTTP at all).
    /// </summary>
    public static readonly IReadOnlyList<string> GenericMirrorHosts =
    [
        "gofile.io", "krakenfiles.com", "megaup.net", "ranoz.gg", "mixdrop.ag",
        "ddownload.com", "1fichier.com", "rapidgator.net", "nitroflare.com", "turbobit.net",
        "4shared.com", "clicknupload.click", "dailyuploads.net", "darkibox.com", "hexload.com",
        "vikingfile.com", "workupload.com", "filer.net", "files.fm", "filemoon.sx",
        "uploadboy.com", "streamtape.com", "savefiles.com", "send.now", "fireload.com",
        "theuser.cloud", "katfile.com", "media.cm", "buzzheavier.com", "chomikuj.pl",
        "transfert.free.fr"
    ];

    private static readonly string[] SupportedHosts =
    [
        "fuckingfast.co", "datanodes.to", "mediafire.com", "multiup.io", "multiup.org",
        .. GenericMirrorHosts
    ];

    private static readonly Regex GenericMirrorUrlRegex = new(
        $@"(?<![\w/.:-])(?:https?://)?(?:www\.)?(?:{string.Join('|', GenericMirrorHosts.Select(Regex.Escape))})/[^\s<>'""\])}}]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<LinkCandidate> ParseMany(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return FuckingFastUrlRegex()
            .Matches(text)
            .Concat(DatanodesUrlRegex().Matches(text))
            .Concat(MediaFireUrlRegex().Matches(text))
            .Concat(MultiUpUrlRegex().Matches(text))
            .Concat(GenericMirrorUrlRegex.Matches(text))
            .Select(match => NormalizeCandidate(match.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ParseUrl)
            .OfType<LinkCandidate>()
            .ToList();
    }

    private static LinkCandidate? ParseUrl(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host["www.".Length..];
        }

        if (host == "multiup.org")
        {
            host = "multiup.io";
        }

        if (!SupportedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var fileName = ExtractFileName(uri, host);
        var (packageName, partNumber) = ExtractArchivePart(fileName);

        return new LinkCandidate(
            rawUrl,
            host,
            fileName,
            packageName,
            partNumber,
            partNumber.HasValue);
    }

    private static string ExtractFileName(Uri uri, string host)
    {
        var fragment = uri.Fragment.TrimStart('#');
        if (!string.IsNullOrWhiteSpace(fragment))
        {
            return Uri.UnescapeDataString(fragment);
        }

        var segments = uri.Segments.Select(segment => segment.Trim('/')).Where(segment => segment.Length > 0).ToList();

        if (string.Equals(host, "mediafire.com", StringComparison.OrdinalIgnoreCase) && segments.Count >= 2)
        {
            var last = segments[^1];
            var candidate = string.Equals(last, "file", StringComparison.OrdinalIgnoreCase)
                ? segments[^2]
                : last;
            return Uri.UnescapeDataString(candidate);
        }

        var slug = segments.LastOrDefault() ?? string.Empty;
        return string.IsNullOrWhiteSpace(slug) ? uri.Host : Uri.UnescapeDataString(slug);
    }

    private static (string PackageName, int? PartNumber) ExtractArchivePart(string fileName)
    {
        var match = ArchivePartRegex().Match(fileName);
        if (!match.Success)
        {
            return (fileName, null);
        }

        return (match.Groups["package"].Value, int.Parse(match.Groups["part"].Value));
    }

    private static string NormalizeCandidate(string value)
    {
        var normalized = value.Trim().TrimEnd('.', ',', ';', ')', ']', '}', '>', '"', '\'');
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "https://" + normalized;
        }

        return normalized;
    }

    [GeneratedRegex(@"(?<![\w/.:-])(?:https?://)?(?:www\.)?fuckingfast\.co/[^\s<>'""\])}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FuckingFastUrlRegex();

    [GeneratedRegex(@"(?<![\w/.:-])(?:https?://)?(?:www\.)?datanodes\.to/[^\s<>'""\])}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DatanodesUrlRegex();

    [GeneratedRegex(@"(?<![\w/.:-])(?:https?://)?(?:www\.)?mediafire\.com/[^\s<>'""\])}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MediaFireUrlRegex();

    [GeneratedRegex(@"(?<![\w/.:-])(?:https?://)?(?:www\.)?multiup\.(?:io|org)/[^\s<>'""\])}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MultiUpUrlRegex();

    [GeneratedRegex(@"^(?<package>.+)\.part(?<part>\d+)\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArchivePartRegex();
}
