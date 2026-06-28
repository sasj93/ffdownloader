namespace FFDownloader.Core.Links;

public static class PackageGrouper
{
    public static IReadOnlyList<DownloadPackage> GroupByPackage(IEnumerable<LinkCandidate> links)
    {
        return links
            .Select((link, index) => new { Link = link, Index = index })
            .GroupBy(entry => entry.Link.PackageName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Min(entry => entry.Index))
            .Select(group =>
            {
                var orderedItems = group
                    .OrderBy(entry => entry.Link.PartNumber ?? int.MaxValue)
                    .ThenBy(entry => entry.Index)
                    .Select(entry => entry.Link)
                    .ToList();

                return new DownloadPackage(group.First().Link.PackageName, orderedItems);
            })
            .ToList();
    }
}
