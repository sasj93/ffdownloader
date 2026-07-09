using FFDownloader.Core.Links;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Links;

public sealed class DownloadLinkParserTests
{
    [Fact]
    public void ParseMany_recognizes_fuckingfast_link_and_decodes_hash_filename()
    {
        var links = DownloadLinkParser.ParseMany(
            "https://fuckingfast.co/64k83eckz3ia#EA_SPORTS_FC_26_--_example.site_--_.part001.rar");

        links.Should().ContainSingle();
        var link = links[0];
        link.Host.Should().Be("fuckingfast.co");
        link.SourceUrl.Should().Be("https://fuckingfast.co/64k83eckz3ia#EA_SPORTS_FC_26_--_example.site_--_.part001.rar");
        link.FileName.Should().Be("EA_SPORTS_FC_26_--_example.site_--_.part001.rar");
        link.PackageName.Should().Be("EA_SPORTS_FC_26_--_example.site_--_");
        link.PartNumber.Should().Be(1);
        link.IsArchivePart.Should().BeTrue();
    }

    [Fact]
    public void ParseMany_extracts_multiple_supported_urls_from_free_text()
    {
        const string text = """
            primeiro link https://fuckingfast.co/64k83eckz3ia#Sample.part001.rar
            segundo link https://fuckingfast.co/vcn5wbjgvcfr#Sample.part002.rar
            fora do host https://example.com/file.zip
            """;

        var links = DownloadLinkParser.ParseMany(text);

        links.Select(link => link.FileName).Should().Equal("Sample.part001.rar", "Sample.part002.rar");
    }

    [Fact]
    public void ParseMany_accepts_markdown_download_list_with_header_and_bullets()
    {
        const string text = """
            ##Download links
            - https://fuckingfast.co/64k83eckz3ia#EA_SPORTS_FC_26_--_fitgirl-repacks.site_--_.part001.rar
            - https://fuckingfast.co/vcn5wbjgvcfr#EA_SPORTS_FC_26_--_fitgirl-repacks.site_--_.part002.rar
            - https://fuckingfast.co/bl815xkuy10e#EA_SPORTS_FC_26_--_fitgirl-repacks.site_--_.part003.rar
            """;

        var links = DownloadLinkParser.ParseMany(text);

        links.Should().HaveCount(3);
        links.Select(link => link.PartNumber).Should().Equal(1, 2, 3);
        links.Should().OnlyContain(link => link.PackageName == "EA_SPORTS_FC_26_--_fitgirl-repacks.site_--_");
    }

    [Fact]
    public void ParseMany_accepts_mixed_list_formats_scheme_less_urls_and_deduplicates()
    {
        const string text = """
            1) fuckingfast.co/aaa111#Game.part001.rar,
            2. <https://www.fuckingfast.co/bbb222#Game.part002.rar>
            3 - [Game part 003](https://fuckingfast.co/ccc333#Game.part003.rar)
            duplicate: https://fuckingfast.co/ccc333#Game.part003.rar.
            """;

        var links = DownloadLinkParser.ParseMany(text);

        links.Select(link => link.SourceUrl).Should().Equal(
            "https://fuckingfast.co/aaa111#Game.part001.rar",
            "https://www.fuckingfast.co/bbb222#Game.part002.rar",
            "https://fuckingfast.co/ccc333#Game.part003.rar");
        links.Select(link => link.PartNumber).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void ParseMany_uses_url_slug_when_hash_filename_is_missing()
    {
        var links = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia");

        links.Should().ContainSingle();
        links[0].FileName.Should().Be("64k83eckz3ia");
        links[0].PackageName.Should().Be("64k83eckz3ia");
        links[0].PartNumber.Should().BeNull();
    }

    [Fact]
    public void ParseMany_recognizes_datanodes_link_and_extracts_filename_from_path()
    {
        var links = DownloadLinkParser.ParseMany(
            "https://datanodes.to/l0ux8t7qvtm3/RDR2_Updated_Setup_Files.part1.rar");

        links.Should().ContainSingle();
        var link = links[0];
        link.Host.Should().Be("datanodes.to");
        link.FileName.Should().Be("RDR2_Updated_Setup_Files.part1.rar");
        link.PackageName.Should().Be("RDR2_Updated_Setup_Files");
        link.PartNumber.Should().Be(1);
        link.IsArchivePart.Should().BeTrue();
    }

    [Fact]
    public void ParseMany_normalizes_www_datanodes_host()
    {
        var links = DownloadLinkParser.ParseMany(
            "https://www.datanodes.to/l0ux8t7qvtm3/RDR2_Updated_Setup_Files.part1.rar");

        links.Should().ContainSingle();
        links[0].Host.Should().Be("datanodes.to");
    }

    [Fact]
    public void ParseMany_extracts_both_fuckingfast_and_datanodes_links_from_mixed_text()
    {
        const string text = """
            https://fuckingfast.co/64k83eckz3ia#Game.part001.rar
            https://datanodes.to/l0ux8t7qvtm3/Game.part002.rar
            """;

        var links = DownloadLinkParser.ParseMany(text);

        links.Should().HaveCount(2);
        links.Select(link => link.Host).Should().Equal("fuckingfast.co", "datanodes.to");
        links.Should().OnlyContain(link => link.PackageName == "Game");
    }

    [Fact]
    public void ParseMany_recognizes_mediafire_link_and_extracts_filename_before_trailing_file_segment()
    {
        var links = DownloadLinkParser.ParseMany(
            "https://www.mediafire.com/file/1w7i8p3u0x1oln9/15_0_2_ZeroXV.exe/file");

        links.Should().ContainSingle();
        var link = links[0];
        link.Host.Should().Be("mediafire.com");
        link.FileName.Should().Be("15_0_2_ZeroXV.exe");
        link.PackageName.Should().Be("15_0_2_ZeroXV.exe");
        link.PartNumber.Should().BeNull();
    }

    [Fact]
    public void ParseMany_recognizes_mediafire_link_without_trailing_file_segment()
    {
        var links = DownloadLinkParser.ParseMany(
            "https://www.mediafire.com/file/1w7i8p3u0x1oln9/Game.part001.rar");

        links.Should().ContainSingle();
        links[0].FileName.Should().Be("Game.part001.rar");
        links[0].PackageName.Should().Be("Game");
        links[0].PartNumber.Should().Be(1);
    }

    [Fact]
    public void ParseMany_recognizes_multiup_link_and_normalizes_org_to_io()
    {
        var links = DownloadLinkParser.ParseMany(
            "https://multiup.org/download/b5f153dbed509301a2407fba8226eb2d/Game.part001.rar");

        links.Should().ContainSingle();
        var link = links[0];
        link.Host.Should().Be("multiup.io");
        link.FileName.Should().Be("Game.part001.rar");
        link.PackageName.Should().Be("Game");
        link.PartNumber.Should().Be(1);
    }

    [Fact]
    public void ParseMany_recognizes_multiup_io_link_directly()
    {
        var links = DownloadLinkParser.ParseMany(
            "https://multiup.io/download/306ea1e41c6b64d9662534687bc66d17/Game.rar");

        links.Should().ContainSingle();
        links[0].Host.Should().Be("multiup.io");
        links[0].FileName.Should().Be("Game.rar");
    }

    [Theory]
    [InlineData("https://gofile.io/d/1Fzh1M")]
    [InlineData("https://krakenfiles.com/view/wPTKGhohqm/file.html")]
    [InlineData("https://megaup.net/abd43beaa36dc458570de7019419a2a8/Game.rar")]
    [InlineData("https://ranoz.gg/file/6MifJZkS")]
    [InlineData("https://mixdrop.ag/f/67pnkrrpsk8mrd")]
    [InlineData("https://1fichier.com/?xo941ohvwsojmdqb0e31")]
    [InlineData("https://rapidgator.net/file/abc123/Game.rar")]
    [InlineData("https://www.krakenfiles.com/view/wPTKGhohqm/file.html")]
    public void ParseMany_recognizes_generic_mirror_hosts(string url)
    {
        var links = DownloadLinkParser.ParseMany(url);

        links.Should().ContainSingle();
        DownloadLinkParser.GenericMirrorHosts.Should().Contain(links[0].Host);
    }

    [Fact]
    public void ParseMany_ignores_infeasible_hosts_like_mega_and_ftp()
    {
        DownloadLinkParser.ParseMany("https://mega.nz/file/abc123#key").Should().BeEmpty();
        DownloadLinkParser.ParseMany("https://drive.google.com/file/d/abc123/view").Should().BeEmpty();
    }

    [Fact]
    public void ParseMany_extracts_fuckingfast_datanodes_and_mediafire_links_from_mixed_text()
    {
        const string text = """
            https://fuckingfast.co/64k83eckz3ia#Game.part001.rar
            https://datanodes.to/l0ux8t7qvtm3/Game.part002.rar
            https://www.mediafire.com/file/1w7i8p3u0x1oln9/Game.part003.rar/file
            """;

        var links = DownloadLinkParser.ParseMany(text);

        links.Should().HaveCount(3);
        links.Select(link => link.Host).Should().Equal("fuckingfast.co", "datanodes.to", "mediafire.com");
        links.Should().OnlyContain(link => link.PackageName == "Game");
    }
}
