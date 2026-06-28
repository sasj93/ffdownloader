using FFDownloader.Core.Downloads;
using FFDownloader.Core.Links;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Downloads;

public sealed class DownloadQueueStoreTests
{
    [Fact]
    public async Task Save_and_load_restores_queue_and_marks_active_items_as_paused_with_disk_progress()
    {
        using var temp = new TempDirectory();
        var partialPath = System.IO.Path.Combine(temp.Path, "Game.part001.rar");
        await File.WriteAllBytesAsync(partialPath, [1, 2, 3]);
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#Game.part001.rar").Single();
        var item = new DownloadItem(link)
        {
            Status = DownloadStatus.Downloading,
            ResolvedUrl = "https://fuckingfast.co/dl/abc/Game.part001.rar",
            LocalPath = partialPath,
            SizeBytes = 5,
            DownloadedBytes = 1,
            SpeedBytesPerSecond = 123
        };
        var package = new DownloadPackageJob("Game", temp.Path, [item])
        {
            Password = "secret",
            AutoExtract = true
        };
        var store = new DownloadQueueStore();
        var statePath = System.IO.Path.Combine(temp.Path, "queue.json");

        store.Save(statePath, [package]);
        var loaded = store.Load(statePath);

        loaded.Should().ContainSingle();
        loaded[0].Name.Should().Be("Game");
        loaded[0].Password.Should().Be("secret");
        loaded[0].AutoExtract.Should().BeTrue();
        loaded[0].Items.Should().ContainSingle();
        loaded[0].Items[0].Status.Should().Be(DownloadStatus.Paused);
        loaded[0].Items[0].DownloadedBytes.Should().Be(3);
        loaded[0].Items[0].SpeedBytesPerSecond.Should().Be(0);
        loaded[0].Items[0].ResolvedUrl.Should().Be("https://fuckingfast.co/dl/abc/Game.part001.rar");
    }

    [Fact]
    public async Task Load_uses_ffdownload_temp_file_when_final_file_is_not_committed()
    {
        using var temp = new TempDirectory();
        var finalPath = System.IO.Path.Combine(temp.Path, "Game.part001.rar");
        await File.WriteAllBytesAsync($"{finalPath}.ffdownload", [1, 2, 3, 4]);
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#Game.part001.rar").Single();
        var item = new DownloadItem(link)
        {
            Status = DownloadStatus.Downloading,
            LocalPath = finalPath,
            SizeBytes = 10
        };
        var package = new DownloadPackageJob("Game", temp.Path, [item]);
        var store = new DownloadQueueStore();
        var statePath = System.IO.Path.Combine(temp.Path, "queue.json");

        store.Save(statePath, [package]);
        var loaded = store.Load(statePath);

        loaded[0].Items[0].Status.Should().Be(DownloadStatus.Paused);
        loaded[0].Items[0].DownloadedBytes.Should().Be(4);
    }

    [Fact]
    public async Task Load_uses_segment_files_when_multi_connection_download_is_partial()
    {
        using var temp = new TempDirectory();
        var finalPath = System.IO.Path.Combine(temp.Path, "Game.part001.rar");
        await File.WriteAllBytesAsync($"{finalPath}.ffdownload.seg000", [1, 2, 3]);
        await File.WriteAllBytesAsync($"{finalPath}.ffdownload.seg001", [4, 5]);
        var link = DownloadLinkParser.ParseMany("https://fuckingfast.co/64k83eckz3ia#Game.part001.rar").Single();
        var item = new DownloadItem(link)
        {
            Status = DownloadStatus.Downloading,
            LocalPath = finalPath,
            SizeBytes = 10
        };
        var package = new DownloadPackageJob("Game", temp.Path, [item]);
        var store = new DownloadQueueStore();
        var statePath = System.IO.Path.Combine(temp.Path, "queue.json");

        store.Save(statePath, [package]);
        var loaded = store.Load(statePath);

        loaded[0].Items[0].Status.Should().Be(DownloadStatus.Paused);
        loaded[0].Items[0].DownloadedBytes.Should().Be(5);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ffdownloader-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
