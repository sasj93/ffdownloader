using FFDownloader.Core.Hosts;
using FFDownloader.Core.Links;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Hosts;

public sealed class ResolverRegistryTests
{
    [Fact]
    public void FindResolver_returns_first_resolver_that_accepts_link()
    {
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#File.rar").Single();
        var registry = new ResolverRegistry([new FakeResolver("fuckingfast.co")]);

        var resolver = registry.FindResolver(link);

        resolver.Should().NotBeNull();
        resolver!.Host.Should().Be("fuckingfast.co");
    }

    [Fact]
    public void FindResolver_returns_null_when_host_is_not_supported()
    {
        var link = new LinkCandidate("https://unsupported.test/file.bin", "unsupported.test", "file.bin", "file.bin", null, false);
        var registry = new ResolverRegistry([new FakeResolver("fuckingfast.co")]);

        registry.FindResolver(link).Should().BeNull();
    }

    private sealed class FakeResolver(string host) : IHostResolver
    {
        public string Host { get; } = host;

        public bool CanResolve(LinkCandidate link) => string.Equals(link.Host, Host, StringComparison.OrdinalIgnoreCase);

        public Task<ResolvedDownload> ResolveAsync(LinkCandidate link, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ResolvedDownload(link.SourceUrl, link.FileName, null));
        }
    }
}
