namespace FFDownloader.Core.Downloads;

public sealed class DownloadPackageJob
{
    private readonly List<DownloadItem> _items = [];

    public DownloadPackageJob(string name, string destinationFolder, IEnumerable<DownloadItem> items)
        : this(Guid.NewGuid(), name, destinationFolder, items)
    {
    }

    public DownloadPackageJob(Guid id, string name, string destinationFolder, IEnumerable<DownloadItem> items)
    {
        Id = id;
        Name = name;
        DestinationFolder = destinationFolder;
        _items.AddRange(OrderItems(items));
    }

    public Guid Id { get; }

    public string Name { get; }

    public string DestinationFolder { get; set; }

    public string? Password { get; set; }

    public bool AutoExtract { get; set; }

    public IReadOnlyList<DownloadItem> Items => _items;

    public long DownloadedBytes => _items.Sum(item => item.DownloadedBytes);

    public long? TotalSizeBytes
    {
        get
        {
            if (_items.Count == 0 || _items.Any(item => !item.SizeBytes.HasValue))
            {
                return null;
            }

            return _items.Sum(item => item.SizeBytes!.Value);
        }
    }

    public double ProgressPercent
    {
        get
        {
            if (_items.Count == 0)
            {
                return 0;
            }

            if (TotalSizeBytes is > 0)
            {
                return Math.Clamp(DownloadedBytes * 100d / TotalSizeBytes.Value, 0, 100);
            }

            return _items.Average(item =>
            {
                if (item.SizeBytes is > 0)
                {
                    return Math.Clamp(item.DownloadedBytes * 100d / item.SizeBytes.Value, 0, 100);
                }

                return item.Status == DownloadStatus.Completed ? 100 : 0;
            });
        }
    }

    public void AddItems(IEnumerable<DownloadItem> items)
    {
        var existing = _items.Select(item => item.SourceUrl).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (existing.Add(item.SourceUrl))
            {
                _items.Add(item);
            }
        }

        _items.Sort(CompareItems);
    }

    private static IEnumerable<DownloadItem> OrderItems(IEnumerable<DownloadItem> items)
    {
        return items.OrderBy(item => item.PartNumber ?? int.MaxValue).ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase);
    }

    private static int CompareItems(DownloadItem left, DownloadItem right)
    {
        var partCompare = (left.PartNumber ?? int.MaxValue).CompareTo(right.PartNumber ?? int.MaxValue);
        return partCompare != 0
            ? partCompare
            : string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase);
    }
}
