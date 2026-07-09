using System.Net;
using System.Text.RegularExpressions;
using FFDownloader.Core.Links;

namespace FFDownloader.Core.Hosts;

public sealed partial class FuckingFastResolver : IHostResolver
{
    private readonly HttpClient _httpClient;

    public FuckingFastResolver()
        : this(new HttpClient())
    {
    }

    public FuckingFastResolver(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string Host => "fuckingfast.co";

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

        var downloadUrl = FindPreferredDownloadUrl(html, link.SourceUrl);
        if (downloadUrl is not null)
        {
            return new ResolvedDownload(downloadUrl, link.FileName, ParseAdvertisedSize(html));
        }

        // Current site layout: the DOWNLOAD anchor issues an htmx POST to /f/{id}/go and the
        // direct dl.fuckingfast.co URL comes back in the HX-Redirect response header.
        var goEndpoint = FindGoEndpoint(html, link.SourceUrl) ?? BuildGoEndpointFromSourceUrl(link.SourceUrl);
        if (goEndpoint is not null)
        {
            var redirected = await TryResolveViaGoEndpointAsync(goEndpoint, link.SourceUrl, cancellationToken);
            if (redirected is not null)
            {
                return new ResolvedDownload(redirected, link.FileName, ParseAdvertisedSize(html));
            }
        }

        downloadUrl = FindFallbackDownloadUrl(html, link.SourceUrl);
        if (downloadUrl is not null)
        {
            return new ResolvedDownload(downloadUrl, link.FileName, ParseAdvertisedSize(html));
        }

        if (!response.IsSuccessStatusCode || LooksLikeChallenge(html))
        {
            throw new ResolveRequiresBrowserException("FuckingFast did not expose the final /dl/ URL to the automatic resolver.");
        }

        throw new ResolveRequiresBrowserException("The final /dl/ download link was not present in the page.");
    }

    public static ResolvedDownload? TryResolveFromHtml(string html, LinkCandidate link)
    {
        var downloadUrl = FindDownloadUrl(html, link.SourceUrl);
        return downloadUrl is null
            ? null
            : new ResolvedDownload(downloadUrl, link.FileName, ParseAdvertisedSize(html));
    }

    public static ResolvedDownload? TryResolveObservedDownloadUrl(string? observedUrl, LinkCandidate link)
    {
        if (string.IsNullOrWhiteSpace(observedUrl))
        {
            return null;
        }

        var downloadUrl = NormalizeUrl(WebUtility.HtmlDecode(observedUrl), link.SourceUrl);
        return IsFuckingFastDownloadUrl(downloadUrl)
            ? new ResolvedDownload(downloadUrl!, link.FileName, null)
            : null;
    }

    private static void ApplyBrowserLikeHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9,pt-BR;q=0.8,pt;q=0.7");
        request.Headers.Referrer = new Uri("https://fuckingfast.co/");
    }

    private static string StripFragment(string sourceUrl)
    {
        var hashIndex = sourceUrl.IndexOf('#', StringComparison.Ordinal);
        return hashIndex >= 0 ? sourceUrl[..hashIndex] : sourceUrl;
    }

    private static bool LooksLikeChallenge(string html)
    {
        return html.Contains("Enable JavaScript and cookies", StringComparison.OrdinalIgnoreCase)
            || html.Contains("cf_chl", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
            || html.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindDownloadUrl(string html, string sourceUrl)
    {
        return FindPreferredDownloadUrl(html, sourceUrl) ?? FindFallbackDownloadUrl(html, sourceUrl);
    }

    private static string? FindPreferredDownloadUrl(string html, string sourceUrl)
    {
        var windowOpen = WindowOpenRegex().Matches(html)
            .Select(match => WebUtility.HtmlDecode(match.Groups["url"].Value))
            .Select(url => NormalizeUrl(url, sourceUrl))
            .FirstOrDefault(url => url is not null && IsUsableDownloadUrl(url, sourceUrl));

        if (windowOpen is not null)
        {
            return windowOpen;
        }

        return DownloadAnchorRegex().Matches(html)
            .Select(match => WebUtility.HtmlDecode(match.Groups["url"].Value))
            .Select(url => NormalizeUrl(url, sourceUrl))
            .FirstOrDefault(url => IsUsableDownloadUrl(url, sourceUrl));
    }

    private static string? FindFallbackDownloadUrl(string html, string sourceUrl)
    {
        return AnyHrefRegex().Matches(html)
            .Select(match => WebUtility.HtmlDecode(match.Groups["url"].Value))
            .Select(url => NormalizeUrl(url, sourceUrl))
            .FirstOrDefault(url => IsUsableDownloadUrl(url, sourceUrl));
    }

    private async Task<string?> TryResolveViaGoEndpointAsync(string goEndpoint, string sourceUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, goEndpoint);
        ApplyBrowserLikeHeaders(request);
        if (Uri.TryCreate(StripFragment(sourceUrl), UriKind.Absolute, out var pageUri))
        {
            request.Headers.Referrer = pageUri;
            request.Headers.TryAddWithoutValidation("Origin", pageUri.GetLeftPart(UriPartial.Authority));
            request.Headers.TryAddWithoutValidation("HX-Current-URL", pageUri.AbsoluteUri);
        }

        request.Headers.TryAddWithoutValidation("HX-Request", "true");
        request.Content = new ByteArrayContent([]);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.Headers.TryGetValues("HX-Redirect", out var redirects))
        {
            var redirected = redirects
                .Select(value => NormalizeUrl(WebUtility.HtmlDecode(value), sourceUrl))
                .FirstOrDefault(url => IsUsableDownloadUrl(url, sourceUrl));
            if (redirected is not null)
            {
                return redirected;
            }
        }

        // A plain 3xx is auto-followed by HttpClient; the final request URI is then the direct file.
        var finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri;
        return IsFuckingFastDownloadUrl(finalUrl) ? finalUrl : null;
    }

    private static string? FindGoEndpoint(string html, string sourceUrl)
    {
        var endpoints = HxPostRegex().Matches(html)
            .Select(match => WebUtility.HtmlDecode(match.Groups["url"].Value))
            .Select(url => NormalizeUrl(url, sourceUrl))
            .Where(url => url is not null)
            .ToArray();

        return endpoints.FirstOrDefault(url => url!.Contains("/f/", StringComparison.OrdinalIgnoreCase)
                || url.EndsWith("/go", StringComparison.OrdinalIgnoreCase))
            ?? endpoints.FirstOrDefault();
    }

    private static string? BuildGoEndpointFromSourceUrl(string sourceUrl)
    {
        if (!Uri.TryCreate(StripFragment(sourceUrl), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var fileId = uri.AbsolutePath.Trim('/');
        if (fileId.Length == 0 || fileId.Contains('/'))
        {
            return null;
        }

        return $"{uri.GetLeftPart(UriPartial.Authority)}/f/{fileId}/go";
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

    private static bool IsUsableDownloadUrl(string? url, string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.Equals(url.TrimEnd('/'), StripFragment(sourceUrl).TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lower = uri.AbsoluteUri.ToLowerInvariant();
        return !lower.Contains("adsterra")
            && !lower.Contains("popads")
            && !lower.Contains("/ads");
    }

    private static bool IsFuckingFastDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Host.EndsWith("fuckingfast.co", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Contains("/dl/", StringComparison.OrdinalIgnoreCase);
    }

    private static long? ParseAdvertisedSize(string html)
    {
        var match = SizeRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }

        var normalizedValue = match.Groups["value"].Value.Replace(',', '.');
        if (!double.TryParse(normalizedValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
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

    [GeneratedRegex(@"window\.open\(\s*[""'](?<url>(?:(?:https?:)?//[^""']+|/dl/[^""']+))[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowOpenRegex();

    [GeneratedRegex(@"<a\b(?=[^>]*(?:download|btn))[^>]*href\s*=\s*[""'](?<url>(?:https?://|/)[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DownloadAnchorRegex();

    [GeneratedRegex(@"href\s*=\s*[""'](?<url>https?://[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnyHrefRegex();

    [GeneratedRegex(@"hx-post\s*=\s*[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HxPostRegex();

    [GeneratedRegex(@"(?:Size\s*:\s*)?(?<value>\d+(?:[\.,]\d+)?)\s*(?<unit>B|KB|MB|GB|TB)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SizeRegex();
}
