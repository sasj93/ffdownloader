using FFDownloader.Core.Downloads;

namespace FFDownloader.App.ViewModels;

public sealed class DownloadItemViewModel : ObservableObject
{
    private DownloadStatus _status;
    private string? _resolvedUrl;
    private string? _localPath;
    private long? _sizeBytes;
    private long _downloadedBytes;
    private double _speedBytesPerSecond;
    private string? _errorMessage;

    public DownloadItemViewModel(DownloadItem model)
    {
        Model = model;
        _status = model.Status;
        _resolvedUrl = model.ResolvedUrl;
        _localPath = model.LocalPath;
        _sizeBytes = model.SizeBytes;
        _downloadedBytes = model.DownloadedBytes;
        _speedBytesPerSecond = model.SpeedBytesPerSecond;
        _errorMessage = model.ErrorMessage;
    }

    public DownloadItem Model { get; }

    public string FileName => Model.FileName;

    public string Host => Model.Host;

    public int? PartNumber => Model.PartNumber;

    public DownloadStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                Model.Status = value;
            }
        }
    }

    public string? ResolvedUrl
    {
        get => _resolvedUrl;
        set
        {
            if (SetProperty(ref _resolvedUrl, value))
            {
                Model.ResolvedUrl = value;
            }
        }
    }

    public string? LocalPath
    {
        get => _localPath;
        set
        {
            if (SetProperty(ref _localPath, value))
            {
                Model.LocalPath = value;
            }
        }
    }

    public long? SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (SetProperty(ref _sizeBytes, value))
            {
                Model.SizeBytes = value;
                OnPropertyChanged(nameof(SizeText));
                OnPropertyChanged(nameof(ProgressPercent));
            }
        }
    }

    public long DownloadedBytes
    {
        get => _downloadedBytes;
        set
        {
            if (SetProperty(ref _downloadedBytes, value))
            {
                Model.DownloadedBytes = value;
                OnPropertyChanged(nameof(DownloadedText));
                OnPropertyChanged(nameof(ProgressPercent));
            }
        }
    }

    public double SpeedBytesPerSecond
    {
        get => _speedBytesPerSecond;
        set
        {
            if (SetProperty(ref _speedBytesPerSecond, value))
            {
                Model.SpeedBytesPerSecond = value;
                OnPropertyChanged(nameof(SpeedText));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                Model.ErrorMessage = value;
            }
        }
    }

    public double ProgressPercent => SizeBytes is > 0 ? Math.Clamp(DownloadedBytes * 100d / SizeBytes.Value, 0, 100) : Status == DownloadStatus.Completed ? 100 : 0;

    public string SizeText => SizeBytes.HasValue ? FormatBytes(SizeBytes.Value) : "-";

    public string DownloadedText => FormatBytes(DownloadedBytes);

    public string SpeedText => SpeedBytesPerSecond > 0 ? $"{FormatBytes((long)SpeedBytesPerSecond)}/s" : "-";

    public void RefreshFromModel()
    {
        Status = Model.Status;
        ResolvedUrl = Model.ResolvedUrl;
        LocalPath = Model.LocalPath;
        SizeBytes = Model.SizeBytes;
        DownloadedBytes = Model.DownloadedBytes;
        SpeedBytesPerSecond = Model.SpeedBytesPerSecond;
        ErrorMessage = Model.ErrorMessage;
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
