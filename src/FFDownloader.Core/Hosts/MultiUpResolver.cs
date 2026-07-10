using System.Net;
using System.Text.RegularExpressions;
using FFDownloader.Core.Links;

namespace FFDownloader.Core.Hosts;

/// <summary>
/// MultiUp is a mirror aggregator: the share page never hosts file bytes itself, it lists the same
/// file mirrored across several third-party hosts. This resolver fetches that mirror list and
/// delegates to whichever already-supported host resolver matches one of the listed mirrors.
/// </summary>
public sealed partial class MultiUpResolver : IHostResolver
{
    private readonly HttpClient _httpClient;
    private readonly ResolverRegistry _mirrorRegistry;

    public MultiUpResolver(ResolverRegistry mirrorRegistry)
        : this(mirrorRegistry, new HttpClient())
    {
    }

    public MultiUpResolver(ResolverRegistry mirrorRegistry, HttpClient httpClient)
    {
        _mirrorRegistry = mirrorRegistry;
        _httpClient = httpClient;
    }

    public string Host => "multiup.io";

    public bool CanResolve(LinkCandidate link)
    {
        return string.Equals(link.Host, Host, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ResolvedDownload> ResolveAsync(LinkCandidate link, CancellationToken cancellationToken)
    {
        using var pageRequest = new HttpRequestMessage(HttpMethod.Get, StripFragment(link.SourceUrl));
        ApplyBrowserLikeHeaders(pageRequest);
        using var pageResponse = await _httpClient.SendAsync(pageRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var pageHtml = await pageResponse.Content.ReadAsStringAsync(cancellationToken);
        var pageUrl = pageResponse.RequestMessage?.RequestUri ?? pageRequest.RequestUri!;

        var mirrorForm = ExtractMirrorForm(pageHtml, pageUrl);
        if (mirrorForm is null)
        {
            throw new ResolveRequiresBrowserException("MultiUp did not expose the mirror-list form to the automatic resolver.");
        }

        using var mirrorContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_csrf_token"] = mirrorForm.Value.CsrfToken
        });
        using var mirrorRequest = new HttpRequestMessage(HttpMethod.Post, mirrorForm.Value.ActionUrl) { Content = mirrorContent };
        ApplyBrowserLikeHeaders(mirrorRequest);
        mirrorRequest.Headers.Referrer = pageUrl;
        using var mirrorResponse = await _httpClient.SendAsync(mirrorRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var mirrorHtml = await mirrorResponse.Content.ReadAsStringAsync(cancellationToken);

        var mirrors = ExtractMirrors(mirrorHtml);
        if (mirrors.Count == 0)
        {
            throw new ResolveRequiresBrowserException("MultiUp did not return any mirror links to the automatic resolver.");
        }

        var failures = new List<string>();
        foreach (var mirror in mirrors)
        {
            var mirrorLink = new LinkCandidate(mirror.Url, mirror.Host, link.FileName, link.PackageName, link.PartNumber, link.IsArchivePart);
            var resolver = _mirrorRegistry.FindResolver(mirrorLink);
            if (resolver is null)
            {
                continue;
            }

            try
            {
                return await resolver.ResolveAsync(mirrorLink, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A mirror can fail for reasons unrelated to the file itself (daily quota hit,
                // temporary outage, dead link on that specific host). MultiUp lists the same file
                // on several hosts precisely so another one can be tried instead of giving up.
                failures.Add($"{mirror.Host}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException($"MultiUp mirrors were all tried and failed: {string.Join(" | ", failures)}");
        }

        var hostList = string.Join(", ", mirrors.Select(mirror => mirror.Host).Distinct(StringComparer.OrdinalIgnoreCase));
        throw new InvalidOperationException($"MultiUp only offers unsupported mirror hosts for this file: {hostList}.");
    }

    private static MirrorForm? ExtractMirrorForm(string html, Uri pageUrl)
    {
        foreach (Match formMatch in FormBlockRegex().Matches(html))
        {
            var body = formMatch.Groups["body"].Value;
            if (!body.Contains("id=\"download-button\"", StringComparison.OrdinalIgnoreCase)
                && !body.Contains("id='download-button'", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var csrfMatch = CsrfTokenRegex().Match(body);
            if (!csrfMatch.Success)
            {
                continue;
            }

            var action = WebUtility.HtmlDecode(formMatch.Groups["action"].Value);
            if (!Uri.TryCreate(pageUrl, action, out var actionUri))
            {
                continue;
            }

            return new MirrorForm(actionUri, WebUtility.HtmlDecode(csrfMatch.Groups["token"].Value));
        }

        return null;
    }

    private static List<MirrorLink> ExtractMirrors(string html)
    {
        var mirrors = new List<MirrorLink>();
        foreach (Match match in MirrorAnchorRegex().Matches(html))
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
            var host = NormalizeMirrorHost(match.Groups["host"].Value);
            if (Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                mirrors.Add(new MirrorLink(host, url));
            }
        }

        return mirrors;
    }

    private static string NormalizeMirrorHost(string host)
    {
        host = host.Trim().ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host["www.".Length..] : host;
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

    private readonly record struct MirrorForm(Uri ActionUrl, string CsrfToken);

    private sealed record MirrorLink(string Host, string Url);

    [GeneratedRegex(@"<form\b[^>]*action=[""'](?<action>[^""']*)[""'][^>]*>(?<body>[\s\S]*?)</form>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FormBlockRegex();

    [GeneratedRegex(@"name=[""']_csrf_token[""']\s+value=[""'](?<token>[^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CsrfTokenRegex();

    [GeneratedRegex(@"<a\b[^>]*href=[""'](?<url>[^""']*)[""'][^>]*nameHost=[""'](?<host>[^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MirrorAnchorRegex();
}
