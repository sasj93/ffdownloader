using System.Net;
using FFDownloader.Core.Hosts;
using FFDownloader.Core.Links;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Hosts;

public sealed class MediaFireResolverTests
{
    [Fact]
    public async Task ResolveAsync_extracts_download_button_href_and_advertised_size()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                <html>
                  <body>
                    <a class="input popsok" aria-label="Download file"
                       href="https://download1580.mediafire.com/abc123/1w7i8p3u0x1oln9/File.exe"
                       id="downloadButton" rel="nofollow">
                        Download (50.27MB)
                    </a>
                  </body>
                </html>
                """)
        });
        var resolver = new MediaFireResolver(new HttpClient(handler));
        var link = DownloadLinkParser.ParseMany("https://www.mediafire.com/file/1w7i8p3u0x1oln9/File.exe/file").Single();

        var resolved = await resolver.ResolveAsync(link, CancellationToken.None);

        resolved.DownloadUrl.Should().Be("https://download1580.mediafire.com/abc123/1w7i8p3u0x1oln9/File.exe");
        resolved.FileName.Should().Be("File.exe");
        resolved.SizeBytes.Should().Be((long)Math.Round(50.27 * 1024 * 1024));
    }

    [Fact]
    public async Task ResolveAsync_finds_download_button_regardless_of_attribute_order()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                <a id="downloadButton" class="input popsok" href="https://download1580.mediafire.com/xyz/File.exe">Download</a>
                """)
        });
        var resolver = new MediaFireResolver(new HttpClient(handler));
        var link = DownloadLinkParser.ParseMany("https://www.mediafire.com/file/1w7i8p3u0x1oln9/File.exe/file").Single();

        var resolved = await resolver.ResolveAsync(link, CancellationToken.None);

        resolved.DownloadUrl.Should().Be("https://download1580.mediafire.com/xyz/File.exe");
    }

    [Fact]
    public async Task ResolveAsync_throws_browser_required_when_download_button_is_missing()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>This file is password protected.</body></html>")
        });
        var resolver = new MediaFireResolver(new HttpClient(handler));
        var link = DownloadLinkParser.ParseMany("https://www.mediafire.com/file/1w7i8p3u0x1oln9/File.exe/file").Single();

        var act = () => resolver.ResolveAsync(link, CancellationToken.None);

        await act.Should().ThrowAsync<ResolveRequiresBrowserException>();
    }

    [Fact]
    public void TryResolveFromHtml_extracts_href_and_size_from_synthetic_html()
    {
        var link = DownloadLinkParser.ParseMany("https://www.mediafire.com/file/1w7i8p3u0x1oln9/File.exe/file").Single();
        var html = """
            <a class="input popsok" href="https://download1580.mediafire.com/abc/File.exe" id="downloadButton">
                Download (1.5GB)
            </a>
            """;

        var resolved = MediaFireResolver.TryResolveFromHtml(html, link);

        resolved.Should().NotBeNull();
        resolved!.DownloadUrl.Should().Be("https://download1580.mediafire.com/abc/File.exe");
        resolved.SizeBytes.Should().Be((long)Math.Round(1.5 * 1024 * 1024 * 1024));
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
