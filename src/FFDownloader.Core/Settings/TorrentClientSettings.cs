namespace FFDownloader.Core.Settings;

public sealed class TorrentClientSettings
{
    public string DownloadFolder { get; set; } = string.Empty;

    public int ListenPort { get; set; } = 51413;

    public long MaxDownloadSpeedBytesPerSecond { get; set; }

    public long MaxUploadSpeedBytesPerSecond { get; set; }

    public bool EnableDht { get; set; } = true;

    public bool EnablePeerExchange { get; set; } = true;

    public bool EnableLocalPeerDiscovery { get; set; } = true;

    public bool EnablePortForwarding { get; set; } = true;

    public double SeedRatioLimit { get; set; }

    public int SeedTimeLimitMinutes { get; set; }

    public bool StopSeedingAtLimit { get; set; }

    public static TorrentClientSettings CreateDefault()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = string.IsNullOrWhiteSpace(userProfile)
            ? Path.Combine(Environment.CurrentDirectory, "Downloads")
            : Path.Combine(userProfile, "Downloads");

        return new TorrentClientSettings
        {
            DownloadFolder = Path.Combine(downloads, "FFDOWNLOADER", "Torrents"),
            ListenPort = 51413,
            MaxDownloadSpeedBytesPerSecond = 0,
            MaxUploadSpeedBytesPerSecond = 0,
            EnableDht = true,
            EnablePeerExchange = true,
            EnableLocalPeerDiscovery = true,
            EnablePortForwarding = true,
            SeedRatioLimit = 0,
            SeedTimeLimitMinutes = 0,
            StopSeedingAtLimit = false
        };
    }

    public void Validate()
    {
        if (ListenPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(ListenPort), "Use a listen port between 1 and 65535.");
        }

        if (MaxDownloadSpeedBytesPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDownloadSpeedBytesPerSecond), "Download speed limit must be zero or positive.");
        }

        if (MaxUploadSpeedBytesPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxUploadSpeedBytesPerSecond), "Upload speed limit must be zero or positive.");
        }

        if (SeedRatioLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SeedRatioLimit), "Seed ratio limit must be zero or positive.");
        }

        if (SeedTimeLimitMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SeedTimeLimitMinutes), "Seed time limit must be zero or positive.");
        }

        if (string.IsNullOrWhiteSpace(DownloadFolder))
        {
            throw new ArgumentException("Download folder is required.", nameof(DownloadFolder));
        }
    }
}
