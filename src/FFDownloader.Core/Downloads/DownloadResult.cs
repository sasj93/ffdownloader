namespace FFDownloader.Core.Downloads;

public sealed record DownloadResult(string LocalPath, long BytesWritten);
