namespace FFDownloader.Core.Links;

public sealed record LinkCandidate(
    string SourceUrl,
    string Host,
    string FileName,
    string PackageName,
    int? PartNumber,
    bool IsArchivePart);
