using FFDownloader.Core.Settings;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Settings;

public sealed class DownloadSettingsTests
{
    [Fact]
    public void CreateDefault_uses_safe_download_manager_defaults()
    {
        var settings = DownloadSettings.CreateDefault();

        settings.MaxConcurrentDownloads.Should().Be(2);
        settings.ConnectionsPerFile.Should().Be(4);
        settings.RetryCount.Should().Be(3);
        settings.SpeedLimitBytesPerSecond.Should().Be(0);
        settings.EnableMultiConnectionDownloads.Should().BeTrue();
        settings.EnableAdaptiveConnectionCount.Should().BeTrue();
        settings.UseTemporaryDownloadFiles.Should().BeTrue();
        settings.ValidateRemoteIdentity.Should().BeTrue();
        settings.RenewExpiredLinks.Should().BeTrue();
        settings.MonitorClipboard.Should().BeTrue();
        settings.AutoExtract.Should().BeFalse();
        settings.DestinationFolder.Should().EndWith("FFDOWNLOADER");
    }

    [Theory]
    [InlineData(0, 1, 3, 0)]
    [InlineData(2, 0, 3, 0)]
    [InlineData(2, 1, -1, 0)]
    [InlineData(2, 1, 3, -1)]
    public void Validate_rejects_invalid_download_limits(int concurrent, int connections, int retries, long speedLimit)
    {
        var settings = new DownloadSettings
        {
            MaxConcurrentDownloads = concurrent,
            ConnectionsPerFile = connections,
            RetryCount = retries,
            SpeedLimitBytesPerSecond = speedLimit,
            DestinationFolder = "D:\\Downloads"
        };

        var act = () => settings.Validate();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
