namespace FFDownloader.Core.Settings;

public sealed class DownloadSettings
{
    public string DestinationFolder { get; set; } = string.Empty;

    public int MaxConcurrentDownloads { get; set; }

    public int ConnectionsPerFile { get; set; }

    public int RetryCount { get; set; }

    public long SpeedLimitBytesPerSecond { get; set; }

    public bool EnableMultiConnectionDownloads { get; set; } = true;

    public bool EnableAdaptiveConnectionCount { get; set; } = true;

    public bool UseTemporaryDownloadFiles { get; set; } = true;

    public bool ValidateRemoteIdentity { get; set; } = true;

    public bool RenewExpiredLinks { get; set; } = true;

    public long MinMultiConnectionSizeBytes { get; set; } = 16L * 1024 * 1024;

    public bool MonitorClipboard { get; set; }

    public bool AutoStart { get; set; }

    public bool AutoExtract { get; set; }

    public bool CreateSubfolderPerPackage { get; set; } = true;

    public static DownloadSettings CreateDefault()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = string.IsNullOrWhiteSpace(userProfile)
            ? Path.Combine(Environment.CurrentDirectory, "Downloads")
            : Path.Combine(userProfile, "Downloads");

        return new DownloadSettings
        {
            DestinationFolder = Path.Combine(downloads, "FFDOWNLOADER"),
            MaxConcurrentDownloads = 2,
            ConnectionsPerFile = 4,
            RetryCount = 3,
            SpeedLimitBytesPerSecond = 0,
            EnableMultiConnectionDownloads = true,
            EnableAdaptiveConnectionCount = true,
            UseTemporaryDownloadFiles = true,
            ValidateRemoteIdentity = true,
            RenewExpiredLinks = true,
            MinMultiConnectionSizeBytes = 16L * 1024 * 1024,
            MonitorClipboard = true,
            AutoStart = false,
            AutoExtract = false,
            CreateSubfolderPerPackage = true
        };
    }

    public void Validate()
    {
        if (MaxConcurrentDownloads is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentDownloads), "Use 1 to 16 simultaneous downloads.");
        }

        if (ConnectionsPerFile is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(ConnectionsPerFile), "Use 1 to 16 connections per file.");
        }

        if (RetryCount is < 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryCount), "Use 0 to 20 retries.");
        }

        if (SpeedLimitBytesPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SpeedLimitBytesPerSecond), "Speed limit must be zero or positive.");
        }

        if (MinMultiConnectionSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinMultiConnectionSizeBytes), "Minimum multi-connection size must be zero or positive.");
        }

        if (string.IsNullOrWhiteSpace(DestinationFolder))
        {
            throw new ArgumentException("Destination folder is required.", nameof(DestinationFolder));
        }
    }
}
