using System.Collections.ObjectModel;
using FFDownloader.Core.Downloads;

namespace FFDownloader.App.ViewModels;

public sealed class PackageViewModel : ObservableObject
{
    private string? _password;
    private bool _autoExtract;

    public PackageViewModel(DownloadPackageJob model)
    {
        Model = model;
        _password = model.Password;
        _autoExtract = model.AutoExtract;
        Items = new ObservableCollection<DownloadItemViewModel>(model.Items.Select(item => new DownloadItemViewModel(item)));
    }

    public DownloadPackageJob Model { get; }

    public Guid Id => Model.Id;

    public string Name => Model.Name;

    public string DestinationFolder
    {
        get => Model.DestinationFolder;
        set
        {
            if (!string.Equals(Model.DestinationFolder, value, StringComparison.Ordinal))
            {
                Model.DestinationFolder = value;
                OnPropertyChanged();
            }
        }
    }

    public string? Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                Model.Password = value;
            }
        }
    }

    public bool AutoExtract
    {
        get => _autoExtract;
        set
        {
            if (SetProperty(ref _autoExtract, value))
            {
                Model.AutoExtract = value;
            }
        }
    }

    public ObservableCollection<DownloadItemViewModel> Items { get; }

    public int FileCount => Items.Count;

    public long DownloadedBytes => Model.DownloadedBytes;

    public long? TotalSizeBytes => Model.TotalSizeBytes;

    public string DownloadedText => FormatBytes(DownloadedBytes);

    public string TotalSizeText => TotalSizeBytes.HasValue ? FormatBytes(TotalSizeBytes.Value) : "-";

    public double ProgressPercent => Model.ProgressPercent;

    public string StatusText
    {
        get
        {
            if (Items.Any(item => item.Status == DownloadStatus.Downloading))
            {
                return "Baixando";
            }

            if (Items.Any(item => item.Status == DownloadStatus.Failed))
            {
                return "Erro";
            }

            if (Items.All(item => item.Status is DownloadStatus.Completed or DownloadStatus.Extracted))
            {
                return AutoExtract ? "Concluido / extraido" : "Concluido";
            }

            if (Items.Any(item => item.Status == DownloadStatus.Resolving))
            {
                return "Resolvendo";
            }

            return "Na fila";
        }
    }

    public void SyncItems()
    {
        foreach (var modelItem in Model.Items)
        {
            if (Items.All(item => item.Model.Id != modelItem.Id))
            {
                Items.Add(new DownloadItemViewModel(modelItem));
            }
        }

        RefreshComputed();
    }

    public void RefreshComputed()
    {
        foreach (var item in Items)
        {
            item.RefreshFromModel();
        }

        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(DownloadedBytes));
        OnPropertyChanged(nameof(TotalSizeBytes));
        OnPropertyChanged(nameof(DownloadedText));
        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(StatusText));
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
