using System.Net;
using FFDownloader.Core.Hosts;
using FFDownloader.Core.Links;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Hosts;

public sealed class FuckingFastResolverTests
{
    [Fact]
    public async Task ResolveAsync_extracts_download_href_from_accessible_page()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                <html>
                  <body>
                    <a class="download-btn" href="https://cdn.example.test/files/File.part001.rar">DOWNLOAD</a>
                  </body>
                </html>
                """)
        });
        var resolver = new FuckingFastResolver(new HttpClient(handler));
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#File.part001.rar").Single();

        var resolved = await resolver.ResolveAsync(link, CancellationToken.None);

        resolved.DownloadUrl.Should().Be("https://cdn.example.test/files/File.part001.rar");
        resolved.FileName.Should().Be("File.part001.rar");
    }

    [Fact]
    public async Task ResolveAsync_extracts_fast_window_open_dl_url_and_advertised_size()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                <html>
                  <body>
                    <button onclick="window.open('https://fuckingfast.co/dl/abc123/File.part001.rar')">DOWNLOAD</button>
                    <p>Size: 500.0MB | Downloads: 11917</p>
                  </body>
                </html>
                """)
        });
        var resolver = new FuckingFastResolver(new HttpClient(handler));
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#File.part001.rar").Single();

        var resolved = await resolver.ResolveAsync(link, CancellationToken.None);

        resolved.DownloadUrl.Should().Be("https://fuckingfast.co/dl/abc123/File.part001.rar");
        resolved.SizeBytes.Should().Be(524_288_000);
    }

    [Fact]
    public async Task ResolveAsync_extracts_dl_link_even_when_status_code_is_forbidden()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""
                <script>
                  window.open("https://fuckingfast.co/dl/forbidden-but-readable/File.part001.rar")
                </script>
                """)
        });
        var resolver = new FuckingFastResolver(new HttpClient(handler));
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#File.part001.rar").Single();

        var resolved = await resolver.ResolveAsync(link, CancellationToken.None);

        resolved.DownloadUrl.Should().Be("https://fuckingfast.co/dl/forbidden-but-readable/File.part001.rar");
    }

    [Fact]
    public async Task ResolveAsync_extracts_dl_subdomain_direct_url()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                <button onclick="window.open('https://dl.fuckingfast.co/dl/abc123/File.part001.rar')">DOWNLOAD</button>
                """)
        });
        var resolver = new FuckingFastResolver(new HttpClient(handler));
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#File.part001.rar").Single();

        var resolved = await resolver.ResolveAsync(link, CancellationToken.None);

        resolved.DownloadUrl.Should().Be("https://dl.fuckingfast.co/dl/abc123/File.part001.rar");
    }

    [Fact]
    public void TryResolveFromHtml_extracts_relative_window_open_dl_url()
    {
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#File.part001.rar").Single();
        var html = """
            <button class="link-button text-5xl gay-button" onclick="window.open('/dl/abc123/File.part001.rar')">
              DOWNLOAD
            </button>
            """;

        var resolved = FuckingFastResolver.TryResolveFromHtml(html, link);

        resolved.Should().NotBeNull();
        resolved!.DownloadUrl.Should().Be("https://fuckingfast.co/dl/abc123/File.part001.rar");
    }

    [Fact]
    public void TryResolveObservedDownloadUrl_normalizes_relative_dl_url_and_rejects_ads()
    {
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#File.part001.rar").Single();

        var resolved = FuckingFastResolver.TryResolveObservedDownloadUrl("/dl/abc123/File.part001.rar", link);
        var advertisement = FuckingFastResolver.TryResolveObservedDownloadUrl("https://adsterra.example.test/pop", link);

        resolved.Should().NotBeNull();
        resolved!.DownloadUrl.Should().Be("https://fuckingfast.co/dl/abc123/File.part001.rar");
        advertisement.Should().BeNull();
    }

    [Fact]
    public void TryResolveFromHtml_extracts_size_from_download_panel_span()
    {
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#File.part001.rar").Single();
        var html = """
            <div>
              <button class="link-button" onclick="window.open('/dl/abc123/File.part001.rar')">DOWNLOAD</button>
              <span>500.0 MB</span>
            </div>
            """;

        var resolved = FuckingFastResolver.TryResolveFromHtml(html, link);

        resolved.Should().NotBeNull();
        resolved!.SizeBytes.Should().Be(524_288_000);
    }

    [Fact]
    public async Task ResolveAsync_sends_browser_like_headers()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                <button onclick="window.open('https://fuckingfast.co/dl/abc123/File.rar')">DOWNLOAD</button>
                """)
        });
        var resolver = new FuckingFastResolver(new HttpClient(handler));
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#File.rar").Single();

        await resolver.ResolveAsync(link, CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.UserAgent.ToString().Should().Contain("Mozilla");
        handler.LastRequest.Headers.Accept.ToString().Should().Contain("text/html");
        handler.LastRequest.Headers.Referrer.Should().Be(new Uri("https://fuckingfast.co/"));
    }

    [Fact]
    public async Task ResolveAsync_throws_browser_required_when_page_is_challenge()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("Enable JavaScript and cookies to continue")
        });
        var resolver = new FuckingFastResolver(new HttpClient(handler));
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#File.rar").Single();

        var act = () => resolver.ResolveAsync(link, CancellationToken.None);

        await act.Should().ThrowAsync<ResolveRequiresBrowserException>();
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }

        public HttpRequestMessage? LastRequest { get; private set; }
    }
}
