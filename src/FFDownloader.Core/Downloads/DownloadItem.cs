using FFDownloader.Core.Links;

namespace FFDownloader.Core.Downloads;

public sealed class DownloadItem
{
    public DownloadItem(LinkCandidate link)
        : this(Guid.NewGuid(), link)
    {
    }

    public DownloadItem(Guid id, LinkCandidate link)
    {
        Id = id;
        Link = link;
        SourceUrl = link.SourceUrl;
        Host = link.Host;
        FileName = link.FileName;
        PackageName = link.PackageName;
        PartNumber = link.PartNumber;
    }

    public Guid Id { get; }

    public LinkCandidate Link { get; }

    public string SourceUrl { get; }

    public string Host { get; }

    public string FileName { get; }

    public string PackageName { get; }

    public int? PartNumber { get; }

    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;

    public string? ResolvedUrl { get; set; }

    public string? LocalPath { get; set; }

    public long? SizeBytes { get; set; }

    public long DownloadedBytes { get; set; }

    public double SpeedBytesPerSecond { get; set; }

    public string? ErrorMessage { get; set; }
}
