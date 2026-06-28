namespace FFDownloader.Core.Links;

public sealed record DownloadPackage(string Name, IReadOnlyList<LinkCandidate> Items);
