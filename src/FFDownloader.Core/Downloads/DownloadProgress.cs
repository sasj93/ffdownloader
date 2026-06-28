namespace FFDownloader.Core.Downloads;

public sealed record DownloadProgress(
    Guid ItemId,
    long DownloadedBytes,
    long? TotalBytes,
    double SpeedBytesPerSecond,
    DownloadStatus Status);
