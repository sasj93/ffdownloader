using MonoTorrent.Client;

namespace FFDownloader.App.ViewModels;

public sealed class TorrentItemViewModel(Guid id, TorrentManager manager, string saveDirectory, bool isMagnet) : ObservableObject
{
    public Guid Id { get; } = id;

    public TorrentManager Manager { get; } = manager;

    public string SaveDirectory { get; } = saveDirectory;

    public bool IsMagnet { get; } = isMagnet;

    public string Name => Manager.HasMetadata ? Manager.Torrent!.Name : (Manager.MagnetLink?.Name ?? "Fetching metadata...");

    public double ProgressPercent => Math.Clamp(Manager.Progress, 0, 100);

    public long? SizeBytes => Manager.HasMetadata ? Manager.Torrent!.Size : Manager.MagnetLink?.Size;

    public long DownloadedBytes => SizeBytes is > 0 ? (long)(ProgressPercent / 100.0 * SizeBytes.Value) : 0;

    public double DownloadSpeedBytesPerSecond => Manager.Monitor.DownloadRate;

    public double UploadSpeedBytesPerSecond => Manager.Monitor.UploadRate;

    public int Seeds => Manager.Peers.Seeds;

    public int Peers => Manager.Peers.Leechs;

    public string StatusText => Manager.State switch
    {
        TorrentState.Stopped => "Stopped",
        TorrentState.Paused => "Paused",
        TorrentState.Starting => "Starting",
        TorrentState.Downloading => "Downloading",
        TorrentState.Seeding => "Seeding",
        TorrentState.Hashing => "Checking files",
        TorrentState.HashingPaused => "Check paused",
        TorrentState.Stopping => "Stopping",
        TorrentState.Error => "Error",
        TorrentState.Metadata or TorrentState.FetchingHashes => "Fetching metadata",
        _ => Manager.State.ToString()
    };

    public string SizeText => SizeBytes.HasValue ? FormatBytes(SizeBytes.Value) : "-";

    public string DownloadedText => FormatBytes(DownloadedBytes);

    public string DownloadSpeedText => DownloadSpeedBytesPerSecond > 0 ? $"{FormatBytes((long)DownloadSpeedBytesPerSecond)}/s" : "-";

    public string UploadSpeedText => UploadSpeedBytesPerSecond > 0 ? $"{FormatBytes((long)UploadSpeedBytesPerSecond)}/s" : "-";

    public string ErrorMessage => Manager.Error?.Exception?.Message ?? string.Empty;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(SizeBytes));
        OnPropertyChanged(nameof(DownloadedBytes));
        OnPropertyChanged(nameof(DownloadSpeedBytesPerSecond));
        OnPropertyChanged(nameof(UploadSpeedBytesPerSecond));
        OnPropertyChanged(nameof(Seeds));
        OnPropertyChanged(nameof(Peers));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(DownloadedText));
        OnPropertyChanged(nameof(DownloadSpeedText));
        OnPropertyChanged(nameof(UploadSpeedText));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
