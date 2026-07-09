using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using FFDownloader.Core.Links;

namespace FFDownloader.Core.Hosts;

public sealed partial class MediaFireResolver : IHostResolver
{
    private readonly HttpClient _httpClient;

    public MediaFireResolver()
        : this(new HttpClient())
    {
    }

    public MediaFireResolver(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string Host => "mediafire.com";

    public bool CanResolve(LinkCandidate link)
    {
        return string.Equals(link.Host, Host, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ResolvedDownload> ResolveAsync(LinkCandidate link, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, StripFragment(link.SourceUrl));
        ApplyBrowserLikeHeaders(request);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        var downloadUrl = ExtractDownloadButtonUrl(html, link.SourceUrl);
        if (downloadUrl is null)
        {
            throw new ResolveRequiresBrowserException("MediaFire did not expose the direct download link to the automatic resolver.");
        }

        return new ResolvedDownload(downloadUrl, link.FileName, ParseAdvertisedSize(html));
    }

    public static ResolvedDownload? TryResolveFromHtml(string html, LinkCandidate link)
    {
        var downloadUrl = ExtractDownloadButtonUrl(html, link.SourceUrl);
        return downloadUrl is null
            ? null
            : new ResolvedDownload(downloadUrl, link.FileName, ParseAdvertisedSize(html));
    }

    private static string? ExtractDownloadButtonUrl(string html, string sourceUrl)
    {
        var tagMatch = DownloadButtonTagRegex().Match(html);
        if (!tagMatch.Success)
        {
            return null;
        }

        var hrefMatch = HrefAttributeRegex().Match(tagMatch.Value);
        if (!hrefMatch.Success)
        {
            return null;
        }

        var url = WebUtility.HtmlDecode(hrefMatch.Groups["url"].Value);
        return NormalizeUrl(url, sourceUrl);
    }

    private static string? NormalizeUrl(string url, string sourceUrl)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute.AbsoluteUri;
        }

        if (Uri.TryCreate(StripFragment(sourceUrl), UriKind.Absolute, out var source)
            && Uri.TryCreate(source, url, out var relative))
        {
            return relative.AbsoluteUri;
        }

        return null;
    }

    private static string StripFragment(string sourceUrl)
    {
        var hashIndex = sourceUrl.IndexOf('#', StringComparison.Ordinal);
        return hashIndex >= 0 ? sourceUrl[..hashIndex] : sourceUrl;
    }

    private static void ApplyBrowserLikeHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    private static long? ParseAdvertisedSize(string html)
    {
        var match = SizeRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }

        var normalizedValue = match.Groups["value"].Value.Replace(',', '.');
        if (!double.TryParse(normalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var multiplier = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "B" => 1d,
            "KB" => 1024d,
            "MB" => 1024d * 1024d,
            "GB" => 1024d * 1024d * 1024d,
            "TB" => 1024d * 1024d * 1024d * 1024d,
            _ => 1d
        };

        return checked((long)Math.Round(value * multiplier));
    }

    [GeneratedRegex(@"<a\b[^>]*id=[""']downloadButton[""'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DownloadButtonTagRegex();

    [GeneratedRegex(@"href=[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HrefAttributeRegex();

    [GeneratedRegex(@"Download\s*\((?<value>\d+(?:[\.,]\d+)?)\s*(?<unit>B|KB|MB|GB|TB)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SizeRegex();
}
