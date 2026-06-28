namespace FFDownloader.Core.Hosts;

public sealed record ResolvedDownload(string DownloadUrl, string FileName, long? SizeBytes);
