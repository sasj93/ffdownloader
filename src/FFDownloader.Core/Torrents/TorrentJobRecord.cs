namespace FFDownloader.Core.Torrents;

public sealed record TorrentJobRecord(
    Guid Id,
    string Source,
    string SaveDirectory,
    DateTimeOffset AddedAt,
    bool IsPaused)
{
    public bool IsMagnet => Source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase);
}
