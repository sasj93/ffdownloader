using FFDownloader.Core.Torrents;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Torrents;

public sealed class TorrentQueueStoreTests
{
    [Fact]
    public void Save_and_load_roundtrips_torrent_job_records()
    {
        using var temp = new TempDirectory();
        var statePath = System.IO.Path.Combine(temp.Path, "torrent-queue.json");
        var records = new[]
        {
            new TorrentJobRecord(
                Guid.NewGuid(),
                "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=Big+Buck+Bunny",
                temp.Path,
                DateTimeOffset.UtcNow,
                IsPaused: false),
            new TorrentJobRecord(
                Guid.NewGuid(),
                System.IO.Path.Combine(temp.Path, "Game.torrent"),
                temp.Path,
                DateTimeOffset.UtcNow,
                IsPaused: true)
        };
        var store = new TorrentQueueStore();

        store.Save(statePath, records);
        var loaded = store.Load(statePath);

        loaded.Should().HaveCount(2);
        loaded[0].IsMagnet.Should().BeTrue();
        loaded[0].IsPaused.Should().BeFalse();
        loaded[1].IsMagnet.Should().BeFalse();
        loaded[1].IsPaused.Should().BeTrue();
    }

    [Fact]
    public void Load_returns_empty_list_when_file_does_not_exist()
    {
        using var temp = new TempDirectory();
        var store = new TorrentQueueStore();

        var loaded = store.Load(System.IO.Path.Combine(temp.Path, "missing.json"));

        loaded.Should().BeEmpty();
    }

    [Fact]
    public void Load_returns_empty_list_when_file_is_corrupted()
    {
        using var temp = new TempDirectory();
        var statePath = System.IO.Path.Combine(temp.Path, "torrent-queue.json");
        File.WriteAllText(statePath, "{ not valid json");
        var store = new TorrentQueueStore();

        var loaded = store.Load(statePath);

        loaded.Should().BeEmpty();
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
