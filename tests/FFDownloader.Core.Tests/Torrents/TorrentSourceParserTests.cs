using FFDownloader.Core.Torrents;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Torrents;

public sealed class TorrentSourceParserTests
{
    private const string SampleMagnet =
        "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=Big+Buck+Bunny&tr=udp://tracker.opentrackr.org:1337";

    [Fact]
    public void ParseMagnetLinks_extracts_single_magnet_uri()
    {
        var links = TorrentSourceParser.ParseMagnetLinks(SampleMagnet);

        links.Should().ContainSingle();
        links[0].Should().Be(SampleMagnet);
    }

    [Fact]
    public void ParseMagnetLinks_extracts_multiple_links_from_mixed_text_and_deduplicates()
    {
        var text = $"""
            Here are some torrents:
            {SampleMagnet}
            magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Other
            duplicate: {SampleMagnet}
            """;

        var links = TorrentSourceParser.ParseMagnetLinks(text);

        links.Should().HaveCount(2);
    }

    [Fact]
    public void ParseMagnetLinks_returns_empty_for_blank_or_unrelated_text()
    {
        TorrentSourceParser.ParseMagnetLinks("").Should().BeEmpty();
        TorrentSourceParser.ParseMagnetLinks("just some regular text").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Game.torrent", true)]
    [InlineData("Game.TORRENT", true)]
    [InlineData(@"C:\Downloads\Game.torrent", true)]
    [InlineData("Game.rar", false)]
    [InlineData("", false)]
    public void IsTorrentFilePath_detects_torrent_extension(string path, bool expected)
    {
        TorrentSourceParser.IsTorrentFilePath(path).Should().Be(expected);
    }
}
