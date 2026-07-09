using FFDownloader.App.Services;
using FFDownloader.Core.Links;
using FluentAssertions;

namespace FFDownloader.App.Tests;

public sealed class GenericBrowserMirrorResolverTests
{
    private readonly GenericBrowserMirrorResolver _resolver = new();

    [Theory]
    [InlineData("gofile.io")]
    [InlineData("krakenfiles.com")]
    [InlineData("megaup.net")]
    [InlineData("ranoz.gg")]
    [InlineData("mixdrop.ag")]
    [InlineData("1fichier.com")]
    [InlineData("rapidgator.net")]
    public void CanResolve_accepts_configured_generic_mirror_hosts(string host)
    {
        var link = new LinkCandidate($"https://{host}/some/path", host, "Game.rar", "Game", null, false);

        _resolver.CanResolve(link).Should().BeTrue();
    }

    [Theory]
    [InlineData("fuckingfast.co")]
    [InlineData("datanodes.to")]
    [InlineData("mediafire.com")]
    [InlineData("mega.nz")]
    [InlineData("drive.google.com")]
    [InlineData("unsupported-host.example")]
    public void CanResolve_rejects_hosts_it_does_not_cover(string host)
    {
        var link = new LinkCandidate($"https://{host}/some/path", host, "Game.rar", "Game", null, false);

        _resolver.CanResolve(link).Should().BeFalse();
    }
}
