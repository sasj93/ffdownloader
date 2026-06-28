using FFDownloader.Core.Downloads;
using FFDownloader.Core.Links;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Downloads;

public sealed class DownloadQueueTests
{
    [Fact]
    public void AddLinks_creates_packages_from_grouped_links_and_marks_items_queued()
    {
        var links = DownloadLinkParser.ParseMany("""
            https://fuckingfast.co/64k83eckz3ia#Game.part001.rar
            https://fuckingfast.co/vcn5wbjgvcfr#Game.part002.rar
            """);
        var queue = new DownloadQueue();

        queue.AddLinks(links, "D:\\Downloads");

        queue.Packages.Should().ContainSingle();
        queue.Packages[0].Name.Should().Be("Game");
        queue.Packages[0].DestinationFolder.Should().Be("D:\\Downloads");
        queue.Packages[0].Items.Should().HaveCount(2);
        queue.Packages[0].Items.Should().OnlyContain(item => item.Status == DownloadStatus.Queued);
    }

    [Fact]
    public void AddLinks_merges_new_parts_into_existing_package_without_duplicates()
    {
        var queue = new DownloadQueue();
        queue.AddLinks(DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#Game.part001.rar"), "D:\\Downloads");

        queue.AddLinks(DownloadLinkParser.ParseMany("""
            https://fuckingfast.co/64k83eckz3ia#Game.part001.rar
            https://fuckingfast.co/vcn5wbjgvcfr#Game.part002.rar
            """), "D:\\Downloads");

        queue.Packages.Should().ContainSingle();
        queue.Packages[0].Items.Select(item => item.FileName).Should().Equal("Game.part001.rar", "Game.part002.rar");
    }

    [Fact]
    public void Package_progress_uses_total_bytes_when_item_sizes_are_known()
    {
        var links = DownloadLinkParser.ParseMany("""
            https://fuckingfast.co/64k83eckz3ia#Game.part001.rar
            https://fuckingfast.co/vcn5wbjgvcfr#Game.part002.rar
            """);
        var items = links.Select(link => new DownloadItem(link)).ToArray();
        items[0].SizeBytes = 100;
        items[0].DownloadedBytes = 50;
        items[1].SizeBytes = 300;
        items[1].DownloadedBytes = 150;
        var package = new DownloadPackageJob("Game", "D:\\Downloads", items);

        package.TotalSizeBytes.Should().Be(400);
        package.DownloadedBytes.Should().Be(200);
        package.ProgressPercent.Should().Be(50);
    }

    [Fact]
    public void ReplacePackages_restores_existing_queue()
    {
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#Game.part001.rar").Single();
        var package = new DownloadPackageJob("Game", "D:\\Downloads", [new DownloadItem(link)]);
        var queue = new DownloadQueue();

        queue.ReplacePackages([package]);

        queue.Packages.Should().ContainSingle();
        queue.Packages[0].Name.Should().Be("Game");
    }
}
