using FFDownloader.Core.Settings;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Settings;

public sealed class TorrentClientSettingsTests
{
    [Fact]
    public void CreateDefault_uses_safe_torrent_client_defaults()
    {
        var settings = TorrentClientSettings.CreateDefault();

        settings.ListenPort.Should().Be(51413);
        settings.MaxDownloadSpeedBytesPerSecond.Should().Be(0);
        settings.MaxUploadSpeedBytesPerSecond.Should().Be(0);
        settings.EnableDht.Should().BeTrue();
        settings.EnablePeerExchange.Should().BeTrue();
        settings.EnableLocalPeerDiscovery.Should().BeTrue();
        settings.EnablePortForwarding.Should().BeTrue();
        settings.SeedRatioLimit.Should().Be(0);
        settings.SeedTimeLimitMinutes.Should().Be(0);
        settings.StopSeedingAtLimit.Should().BeFalse();
        settings.DownloadFolder.Should().EndWith("Torrents");
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(70000, 0, 0, 0, 0)]
    [InlineData(51413, -1, 0, 0, 0)]
    [InlineData(51413, 0, -1, 0, 0)]
    [InlineData(51413, 0, 0, -1, 0)]
    [InlineData(51413, 0, 0, 0, -1)]
    public void Validate_rejects_invalid_torrent_limits(int port, long maxDown, long maxUp, double seedRatio, int seedMinutes)
    {
        var settings = new TorrentClientSettings
        {
            ListenPort = port,
            MaxDownloadSpeedBytesPerSecond = maxDown,
            MaxUploadSpeedBytesPerSecond = maxUp,
            SeedRatioLimit = seedRatio,
            SeedTimeLimitMinutes = seedMinutes,
            DownloadFolder = "D:\\Downloads\\Torrents"
        };

        var act = () => settings.Validate();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validate_rejects_missing_download_folder()
    {
        var settings = TorrentClientSettings.CreateDefault();
        settings.DownloadFolder = "";

        var act = () => settings.Validate();

        act.Should().Throw<ArgumentException>();
    }
}
