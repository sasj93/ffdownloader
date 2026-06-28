namespace FFDownloader.Core.Extraction;

public sealed record ExtractionResult(string DestinationFolder, IReadOnlyList<string> Files);
