using System.Net;
using System.Text;
using FFDownloader.Core.Hosts;
using FFDownloader.Core.Links;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Hosts;

public sealed class DatanodesResolverTests
{
    private const string PageHtml = """
        <html>
        <body>
        <div class="scan-card" data-scan-file="Game.part1.rar" data-scan-size="500.0 MB">
        <div id="downloadReveal">
        <form method="POST" action='' id="downloadForm" class="m-0 w-full">
            <input type="hidden" name="op" value="download1">
            <input type="hidden" name="usr_login" value="">
            <input type="hidden" name="id" value="abc123">
            <input type="hidden" name="fname" value="Game.part1.rar">
            <input type="hidden" name="referer" value="">
            <button type="submit" id="method_free" name="method_free" value="Free Download &gt;&gt;">Continue</button>
        </form>
        </div>
        </div>
        </body>
        </html>
        """;

    private const string CountdownHtml = """
        <html>
        <body>
        <download-countdown :countdown="5"
            code="abc123" referer="https://datanodes.to/download" rand=""
            free-method="Free Download &gt;&gt;" premium-method=""
            :has-captcha="false" :has-password="false"
            name="Game.part1.rar">
        </download-countdown>
        </body>
        </html>
        """;

    private const string CountdownHtmlWithCaptcha = """
        <html>
        <body>
        <download-countdown :countdown="5"
            code="abc123" referer="https://datanodes.to/download" rand=""
            free-method="Free Download &gt;&gt;" premium-method=""
            :has-captcha="true" :has-password="false"
            name="Game.part1.rar">
        </download-countdown>
        </body>
        </html>
        """;

    [Fact]
    public async Task ResolveAsync_completes_the_three_step_flow_and_decodes_the_final_url()
    {
        var handler = new RoutedHttpMessageHandler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PageHtml) };
            }

            var body = await request.Content!.ReadAsStringAsync();
            if (body.Contains("download1"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CountdownHtml) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"url":"https%3A%2F%2Ftunnel5.dlproxy.uk%2Fdownload%2Ftoken%3Fsig%3Dabc"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var resolver = new DatanodesResolver(handler);
        var link = DownloadLinkParser.ParseMany("https://datanodes.to/abc123/Game.part1.rar").Single();

        var resolved = await resolver.ResolveAsync(link, CancellationToken.None);

        resolved.DownloadUrl.Should().Be("https://tunnel5.dlproxy.uk/download/token?sig=abc");
        resolved.FileName.Should().Be("Game.part1.rar");
        resolved.SizeBytes.Should().Be(524_288_000);
    }

    [Fact]
    public async Task ResolveAsync_sends_download1_then_download2_with_countdown_fields()
    {
        var postedBodies = new List<string>();
        var handler = new RoutedHttpMessageHandler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PageHtml) };
            }

            postedBodies.Add(await request.Content!.ReadAsStringAsync());
            return postedBodies.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CountdownHtml) }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"url":"https%3A%2F%2Ftunnel5.dlproxy.uk%2Fdownload%2Ftoken"}""", Encoding.UTF8, "application/json")
                };
        });

        var resolver = new DatanodesResolver(handler);
        var link = DownloadLinkParser.ParseMany("https://datanodes.to/abc123/Game.part1.rar").Single();

        await resolver.ResolveAsync(link, CancellationToken.None);

        postedBodies.Should().HaveCount(2);
        postedBodies[0].Should().Contain("op=download1").And.Contain("id=abc123").And.Contain("fname=Game.part1.rar");
        postedBodies[1].Should().Contain("name=op").And.Contain("download2").And.Contain("name=id").And.Contain("abc123");
    }

    [Fact]
    public async Task ResolveAsync_throws_browser_required_when_captcha_is_required()
    {
        var handler = new RoutedHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PageHtml) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CountdownHtmlWithCaptcha) });
        });

        var resolver = new DatanodesResolver(handler);
        var link = DownloadLinkParser.ParseMany("https://datanodes.to/abc123/Game.part1.rar").Single();

        var act = () => resolver.ResolveAsync(link, CancellationToken.None);

        await act.Should().ThrowAsync<ResolveRequiresBrowserException>();
    }

    [Fact]
    public async Task ResolveAsync_throws_browser_required_when_download_form_is_missing()
    {
        var handler = new RoutedHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>Not found</body></html>")
        }));

        var resolver = new DatanodesResolver(handler);
        var link = DownloadLinkParser.ParseMany("https://datanodes.to/abc123/Game.part1.rar").Single();

        var act = () => resolver.ResolveAsync(link, CancellationToken.None);

        await act.Should().ThrowAsync<ResolveRequiresBrowserException>();
    }

    [Fact]
    public async Task ResolveAsync_throws_browser_required_when_final_url_is_missing_from_response()
    {
        var handler = new RoutedHttpMessageHandler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PageHtml) };
            }

            var body = await request.Content!.ReadAsStringAsync();
            if (body.Contains("download1"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CountdownHtml) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"error":"rate limited"}""") };
        });

        var resolver = new DatanodesResolver(handler);
        var link = DownloadLinkParser.ParseMany("https://datanodes.to/abc123/Game.part1.rar").Single();

        var act = () => resolver.ResolveAsync(link, CancellationToken.None);

        await act.Should().ThrowAsync<ResolveRequiresBrowserException>();
    }

    private sealed class RoutedHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return responder(request);
        }
    }
}
