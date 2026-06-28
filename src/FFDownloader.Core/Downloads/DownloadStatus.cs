namespace FFDownloader.Core.Downloads;

public enum DownloadStatus
{
    Queued,
    Resolving,
    Downloading,
    Paused,
    Completed,
    Failed,
    Canceled,
    Extracting,
    Extracted
}
