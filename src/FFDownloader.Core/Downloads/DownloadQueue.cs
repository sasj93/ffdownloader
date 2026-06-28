using FFDownloader.Core.Links;

namespace FFDownloader.Core.Downloads;

public sealed class DownloadQueue
{
    private readonly List<DownloadPackageJob> _packages = [];

    public IReadOnlyList<DownloadPackageJob> Packages => _packages;

    public IReadOnlyList<DownloadPackageJob> AddLinks(IEnumerable<LinkCandidate> links, string destinationFolder)
    {
        var addedOrUpdated = new List<DownloadPackageJob>();

        foreach (var groupedPackage in PackageGrouper.GroupByPackage(links))
        {
            var existing = _packages.FirstOrDefault(package =>
                string.Equals(package.Name, groupedPackage.Name, StringComparison.OrdinalIgnoreCase));

            var items = groupedPackage.Items.Select(link => new DownloadItem(link)).ToList();
            if (existing is null)
            {
                existing = new DownloadPackageJob(groupedPackage.Name, destinationFolder, items);
                _packages.Add(existing);
            }
            else
            {
                existing.AddItems(items);
            }

            addedOrUpdated.Add(existing);
        }

        return addedOrUpdated;
    }

    public void RemovePackage(Guid packageId)
    {
        _packages.RemoveAll(package => package.Id == packageId);
    }

    public void ReplacePackages(IEnumerable<DownloadPackageJob> packages)
    {
        _packages.Clear();
        _packages.AddRange(packages);
    }

    public void ClearCompleted()
    {
        _packages.RemoveAll(package => package.Items.All(item => item.Status is DownloadStatus.Completed or DownloadStatus.Extracted));
    }
}
