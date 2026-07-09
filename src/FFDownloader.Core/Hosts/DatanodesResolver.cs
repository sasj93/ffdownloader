using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FFDownloader.Core.Links;

namespace FFDownloader.Core.Hosts;

public sealed partial class DatanodesResolver : IHostResolver
{
    private readonly Func<HttpMessageHandler>? _testHandlerFactory;

    public DatanodesResolver()
    {
    }

    public DatanodesResolver(HttpMessageHandler testHandler)
    {
        _testHandlerFactory = () => testHandler;
    }

    public string Host => "datanodes.to";

    public bool CanResolve(LinkCandidate link)
    {
        return string.Equals(link.Host, Host, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ResolvedDownload> ResolveAsync(LinkCandidate link, CancellationToken cancellationToken)
    {
        using var httpClient = CreateHttpClient();

        using var pageRequest = new HttpRequestMessage(HttpMethod.Get, StripFragment(link.SourceUrl));
        ApplyBrowserLikeHeaders(pageRequest);
        using var pageResponse = await httpClient.SendAsync(pageRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var pageHtml = await pageResponse.Content.ReadAsStringAsync(cancellationToken);
        var pageUrl = pageResponse.RequestMessage?.RequestUri ?? pageRequest.RequestUri!;

        var fields = ExtractHiddenFields(pageHtml);
        var freeMethodLabel = ExtractMethodFreeValue(pageHtml);
        if (fields is null || freeMethodLabel is null)
        {
            throw new ResolveRequiresBrowserException("Datanodes did not expose the initial download form to the automatic resolver.");
        }

        var advertisedSize = ParseAdvertisedSize(pageHtml);

        using var step1Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["op"] = "download1",
            ["usr_login"] = fields.GetValueOrDefault("usr_login", string.Empty),
            ["id"] = fields.GetValueOrDefault("id", ExtractFileCode(link)),
            ["fname"] = fields.GetValueOrDefault("fname", link.FileName),
            ["referer"] = fields.GetValueOrDefault("referer", string.Empty),
            ["method_free"] = freeMethodLabel
        });

        using var step1Request = new HttpRequestMessage(HttpMethod.Post, pageUrl) { Content = step1Content };
        ApplyBrowserLikeHeaders(step1Request);
        using var step1Response = await httpClient.SendAsync(step1Request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var step1Html = await step1Response.Content.ReadAsStringAsync(cancellationToken);

        var countdown = ExtractCountdownAttributes(step1Html);
        if (countdown is null)
        {
            throw new ResolveRequiresBrowserException("Datanodes did not expose the download countdown step to the automatic resolver.");
        }

        if (countdown.HasCaptcha || countdown.HasPassword)
        {
            throw new ResolveRequiresBrowserException("Datanodes requires captcha or password confirmation; falling back to the browser resolver.");
        }

        using var step2Content = new MultipartFormDataContent
        {
            { new StringContent("download2"), "op" },
            { new StringContent(countdown.Code), "id" },
            { new StringContent(countdown.Rand), "rand" },
            { new StringContent(countdown.Referer), "referer" },
            { new StringContent(countdown.FreeMethod), "method_free" },
            { new StringContent(countdown.PremiumMethod), "method_premium" },
            { new StringContent("1"), "g_captch__a" }
        };

        using var step2Request = new HttpRequestMessage(HttpMethod.Post, pageUrl) { Content = step2Content };
        ApplyBrowserLikeHeaders(step2Request);
        using var step2Response = await httpClient.SendAsync(step2Request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var step2Json = await step2Response.Content.ReadAsStringAsync(cancellationToken);

        var downloadUrl = ParseDownloadUrl(step2Json);
        if (downloadUrl is null)
        {
            throw new ResolveRequiresBrowserException("Datanodes did not return a direct download URL to the automatic resolver.");
        }

        return new ResolvedDownload(downloadUrl, link.FileName, advertisedSize);
    }

    private HttpClient CreateHttpClient()
    {
        if (_testHandlerFactory is not null)
        {
            return new HttpClient(_testHandlerFactory(), disposeHandler: false);
        }

        return new HttpClient(new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true
        });
    }

    private static void ApplyBrowserLikeHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    private static string StripFragment(string sourceUrl)
    {
        var hashIndex = sourceUrl.IndexOf('#', StringComparison.Ordinal);
        return hashIndex >= 0 ? sourceUrl[..hashIndex] : sourceUrl;
    }

    private static string ExtractFileCode(LinkCandidate link)
    {
        if (Uri.TryCreate(StripFragment(link.SourceUrl), UriKind.Absolute, out var uri))
        {
            var segment = uri.Segments.Skip(1).FirstOrDefault()?.Trim('/');
            if (!string.IsNullOrWhiteSpace(segment))
            {
                return segment;
            }
        }

        return link.FileName;
    }

    private static Dictionary<string, string>? ExtractHiddenFields(string html)
    {
        var formMatch = DownloadFormRegex().Match(html);
        if (!formMatch.Success)
        {
            return null;
        }

        var body = formMatch.Groups["body"].Value;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in HiddenInputRegex().Matches(body))
        {
            fields[match.Groups["name"].Value] = WebUtility.HtmlDecode(match.Groups["value"].Value);
        }

        return fields;
    }

    private static string? ExtractMethodFreeValue(string html)
    {
        var match = MethodFreeRegex().Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
    }

    private static CountdownInfo? ExtractCountdownAttributes(string html)
    {
        var tagMatch = CountdownTagRegex().Match(html);
        if (!tagMatch.Success)
        {
            return null;
        }

        var tag = tagMatch.Value;
        var code = ExtractAttribute(tag, "code");
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        return new CountdownInfo(
            code,
            ExtractAttribute(tag, "referer") ?? string.Empty,
            ExtractAttribute(tag, "rand") ?? string.Empty,
            ExtractAttribute(tag, "free-method") ?? string.Empty,
            ExtractAttribute(tag, "premium-method") ?? string.Empty,
            string.Equals(ExtractAttribute(tag, "has-captcha"), "true", StringComparison.OrdinalIgnoreCase),
            string.Equals(ExtractAttribute(tag, "has-password"), "true", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractAttribute(string tag, string attributeName)
    {
        var pattern = $@"(?<![\w-]):?{Regex.Escape(attributeName)}\s*=\s*[""'](?<value>[^""']*)[""']";
        var match = Regex.Match(tag, pattern, RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
    }

    private static long? ParseAdvertisedSize(string html)
    {
        var match = ScanSizeRegex().Match(html);
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

    private static string? ParseDownloadUrl(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<DownloadUrlResponse>(json);
            if (string.IsNullOrWhiteSpace(parsed?.Url))
            {
                return null;
            }

            var decoded = Uri.UnescapeDataString(parsed.Url);
            return Uri.TryCreate(decoded, UriKind.Absolute, out var uri) ? uri.AbsoluteUri : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record CountdownInfo(
        string Code,
        string Referer,
        string Rand,
        string FreeMethod,
        string PremiumMethod,
        bool HasCaptcha,
        bool HasPassword);

    private sealed record DownloadUrlResponse([property: JsonPropertyName("url")] string? Url);

    [GeneratedRegex(@"<form\b[^>]*id=[""']downloadForm[""'][^>]*>(?<body>[\s\S]*?)</form>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DownloadFormRegex();

    [GeneratedRegex(@"<input\s+type=[""']hidden[""']\s+name=[""'](?<name>[^""']+)[""']\s+value=[""'](?<value>[^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HiddenInputRegex();

    [GeneratedRegex(@"id=[""']method_free[""'][^>]*value=[""'](?<value>[^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MethodFreeRegex();

    [GeneratedRegex(@"<download-countdown\b[\s\S]*?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CountdownTagRegex();

    [GeneratedRegex(@"data-scan-size=[""'](?<value>[\d.,]+)\s*(?<unit>B|KB|MB|GB|TB)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScanSizeRegex();
}
