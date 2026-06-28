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
}
