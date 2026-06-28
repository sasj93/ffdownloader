using FFDownloader.Core.Links;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Links;

public sealed class PackageGrouperTests
{
    [Fact]
    public void GroupByPackage_groups_rar_parts_under_same_package_and_orders_by_part_number()
    {
        var links = DownloadLinkParser.ParseMany("""
            https://fuckingfast.co/bl815xkuy10e#Game.part003.rar
            https://fuckingfast.co/64k83eckz3ia#Game.part001.rar
            https://fuckingfast.co/vcn5wbjgvcfr#Game.part002.rar
            """);

        var packages = PackageGrouper.GroupByPackage(links);

        packages.Should().ContainSingle();
        packages[0].Name.Should().Be("Game");
        packages[0].Items.Select(item => item.PartNumber).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void GroupByPackage_keeps_unrelated_files_in_separate_packages()
    {
        var links = DownloadLinkParser.ParseMany("""
            https://fuckingfast.co/64k83eckz3ia#Game.part001.rar
            https://fuckingfast.co/aabbccddeeff#Movie.mkv
            """);

        var packages = PackageGrouper.GroupByPackage(links);

        packages.Select(package => package.Name).Should().Equal("Game", "Movie.mkv");
    }
}
