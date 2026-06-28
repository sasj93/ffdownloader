using System.Text.Json;
using FFDownloader.Core.Links;

namespace FFDownloader.Core.Downloads;

public sealed class DownloadQueueStore
{
    private const int CurrentVersion = 1;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<DownloadPackageJob> Load(string statePath)
    {
        if (!File.Exists(statePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(statePath);
            var state = JsonSerializer.Deserialize<QueueState>(json, _jsonOptions);
            if (state?.Packages is null)
            {
                return [];
            }

            return state.Packages.Select(RestorePackage).ToList();
        }
        catch
        {
            return [];
        }
    }

    public void Save(string statePath, IReadOnlyList<DownloadPackageJob> packages)
    {
        var directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var state = new QueueState(
            CurrentVersion,
            packages.Select(package => new PackageState(
                package.Id,
                package.Name,
                package.DestinationFolder,
                package.Password,
                package.AutoExtract,
                package.Items.Select(item => new ItemState(
                    item.Id,
                    item.SourceUrl,
                    item.Host,
                    item.FileName,
                    item.PackageName,
                    item.PartNumber,
                    item.Link.IsArchivePart,
                    item.Status,
                    item.ResolvedUrl,
                    item.LocalPath,
                    item.SizeBytes,
                    item.DownloadedBytes,
                    item.ErrorMessage)).ToList())).ToList());

        File.WriteAllText(statePath, JsonSerializer.Serialize(state, _jsonOptions));
    }

    private static DownloadPackageJob RestorePackage(PackageState packageState)
    {
        var items = packageState.Items.Select(RestoreItem).ToList();
        return new DownloadPackageJob(packageState.Id, packageState.Name, packageState.DestinationFolder, items)
        {
            Password = packageState.Password,
            AutoExtract = packageState.AutoExtract
        };
    }

    private static DownloadItem RestoreItem(ItemState itemState)
    {
        var link = new LinkCandidate(
            itemState.SourceUrl,
            itemState.Host,
            itemState.FileName,
            itemState.PackageName,
            itemState.PartNumber,
            itemState.IsArchivePart);

        var item = new DownloadItem(itemState.Id, link)
        {
            Status = NormalizeStatus(itemState.Status),
            ResolvedUrl = itemState.ResolvedUrl,
            LocalPath = itemState.LocalPath,
            SizeBytes = itemState.SizeBytes,
            DownloadedBytes = Math.Max(0, itemState.DownloadedBytes),
            SpeedBytesPerSecond = 0,
            ErrorMessage = itemState.ErrorMessage
        };

        RefreshDiskProgress(item);
        return item;
    }

    private static DownloadStatus NormalizeStatus(DownloadStatus status)
    {
        return status is DownloadStatus.Downloading or DownloadStatus.Resolving or DownloadStatus.Extracting
            ? DownloadStatus.Paused
            : status;
    }

    private static void RefreshDiskProgress(DownloadItem item)
    {
        if (string.IsNullOrWhiteSpace(item.LocalPath))
        {
            item.DownloadedBytes = item.Status is DownloadStatus.Completed && item.SizeBytes is > 0
                ? item.SizeBytes.Value
                : Math.Max(0, item.DownloadedBytes);
            return;
        }

        if (!File.Exists(item.LocalPath))
        {
            var partialBytes = GetTemporaryPartialBytes(item.LocalPath);
            item.DownloadedBytes = partialBytes;
            if (partialBytes > 0)
            {
                item.Status = item.Status == DownloadStatus.Extracted ? DownloadStatus.Extracted : DownloadStatus.Paused;
                return;
            }

            if (item.Status is DownloadStatus.Completed or DownloadStatus.Paused)
            {
                item.Status = DownloadStatus.Queued;
            }

            item.DownloadedBytes = 0;
            return;
        }

        var diskLength = new FileInfo(item.LocalPath).Length;
        item.DownloadedBytes = diskLength;

        if (item.SizeBytes is > 0 && diskLength >= item.SizeBytes.Value)
        {
            item.DownloadedBytes = item.SizeBytes.Value;
            if (item.Status != DownloadStatus.Extracted)
            {
                item.Status = DownloadStatus.Completed;
            }
        }
    }

    private static long GetTemporaryPartialBytes(string finalPath)
    {
        var tempPath = $"{finalPath}.ffdownload";
        if (File.Exists(tempPath))
        {
            return new FileInfo(tempPath).Length;
        }

        var directory = Path.GetDirectoryName(tempPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return 0;
        }

        return Directory.EnumerateFiles(directory, $"{Path.GetFileName(tempPath)}.seg*")
            .Sum(path => new FileInfo(path).Length);
    }

    private sealed record QueueState(int Version, List<PackageState> Packages);

    private sealed record PackageState(
        Guid Id,
        string Name,
        string DestinationFolder,
        string? Password,
        bool AutoExtract,
        List<ItemState> Items);

    private sealed record ItemState(
        Guid Id,
        string SourceUrl,
        string Host,
        string FileName,
        string PackageName,
        int? PartNumber,
        bool IsArchivePart,
        DownloadStatus Status,
        string? ResolvedUrl,
        string? LocalPath,
        long? SizeBytes,
        long DownloadedBytes,
        string? ErrorMessage);
}
