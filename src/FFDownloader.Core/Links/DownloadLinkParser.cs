using System.Text.RegularExpressions;

namespace FFDownloader.Core.Links;

public static partial class DownloadLinkParser
{
    public static IReadOnlyList<LinkCandidate> ParseMany(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return UrlRegex()
            .Matches(text)
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
        if (host is "www.fuckingfast.co")
        {
            host = "fuckingfast.co";
        }

        if (host is not "fuckingfast.co")
        {
            return null;
        }

        var fileName = ExtractFileName(uri);
        var (packageName, partNumber) = ExtractArchivePart(fileName);

        return new LinkCandidate(
            rawUrl,
            host,
            fileName,
            packageName,
            partNumber,
            partNumber.HasValue);
    }

    private static string ExtractFileName(Uri uri)
    {
        var fragment = uri.Fragment.TrimStart('#');
        if (!string.IsNullOrWhiteSpace(fragment))
        {
            return Uri.UnescapeDataString(fragment);
        }

        var slug = uri.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;
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
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^(?<package>.+)\.part(?<part>\d+)\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArchivePartRegex();
}
