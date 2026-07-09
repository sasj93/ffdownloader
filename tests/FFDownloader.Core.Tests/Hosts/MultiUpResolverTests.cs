using System.Net;
using FFDownloader.Core.Hosts;
using FFDownloader.Core.Links;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Hosts;

public sealed class MultiUpResolverTests
{
    private const string SharePageHtml = """
        <html>
        <body>
        <form action="/en/mirror/abc123" method="post" target="757015368" onsubmit="window.open('','757015368')">
            <a href="/download-fast/abc123/Game.rar" class="btn btn-success">Download faster on USENET</a>
            <input type="hidden" class="hidden" name="_csrf_token" value="token-value-123" />
            <button id="download-button" class="btn btn-info" type="submit"><strong>Download</strong></button>
        </form>
        </body>
        </html>
        """;

    [Fact]
    public async Task ResolveAsync_delegates_to_a_supported_mirror_host()
    {
        var mirrorListHtml = """
            <html><body>
            <a href="/download-fast/abc123/Game.rar" nameHost="UseNet" id="1">Download</a>
            <a href="https://gofile.io/d/xyz" target="_blank" nameHost="gofile.io" id="2">Download</a>
            <a href="https://fuckingfast.co/tok3n#Game.rar" target="_blank" nameHost="fuckingfast.co" id="3">Download</a>
            </body></html>
            """;
        var multiUpHandler = new RoutedHttpMessageHandler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SharePageHtml) };
            }

            var body = await request.Content!.ReadAsStringAsync();
            body.Should().Contain("_csrf_token=token-value-123");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(mirrorListHtml) };
        });

        var fuckingFastHandler = new RoutedHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""<button onclick="window.open('https://fuckingfast.co/dl/abc123/Game.rar')">DOWNLOAD</button>""")
        }));

        var mirrorRegistry = new ResolverRegistry([new FuckingFastResolver(new HttpClient(fuckingFastHandler))]);
        var resolver = new MultiUpResolver(mirrorRegistry, new HttpClient(multiUpHandler));
        var link = DownloadLinkParser.ParseMany("https://multiup.io/download/abc123/Game.rar").Single();

        var resolved = await resolver.ResolveAsync(link, CancellationToken.None);

        resolved.DownloadUrl.Should().Be("https://fuckingfast.co/dl/abc123/Game.rar");
        resolved.FileName.Should().Be("Game.rar");
    }

    [Fact]
    public async Task ResolveAsync_throws_with_unsupported_host_list_when_no_mirror_matches()
    {
        var mirrorListHtml = """
            <html><body>
            <a href="/download-fast/abc123/Game.rar" nameHost="UseNet" id="1">Download</a>
            <a href="https://gofile.io/d/xyz" target="_blank" nameHost="gofile.io" id="2">Download</a>
            <a href="https://mixdrop.ag/f/token" target="_blank" nameHost="mixdrop.ag" id="3">Download</a>
            </body></html>
            """;
        var handler = new RoutedHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SharePageHtml) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(mirrorListHtml) });
        });

        var mirrorRegistry = new ResolverRegistry([new FuckingFastResolver(), new DatanodesResolver(), new MediaFireResolver()]);
        var resolver = new MultiUpResolver(mirrorRegistry, new HttpClient(handler));
        var link = DownloadLinkParser.ParseMany("https://multiup.io/download/abc123/Game.rar").Single();

        var act = () => resolver.ResolveAsync(link, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*gofile.io*mixdrop.ag*");
    }

    [Fact]
    public async Task ResolveAsync_throws_browser_required_when_mirror_form_is_missing()
    {
        var handler = new RoutedHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>File not found.</body></html>")
        }));

        var mirrorRegistry = new ResolverRegistry([new FuckingFastResolver()]);
        var resolver = new MultiUpResolver(mirrorRegistry, new HttpClient(handler));
        var link = DownloadLinkParser.ParseMany("https://multiup.io/download/abc123/Game.rar").Single();

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
