using FFDownloader.Core.Links;

namespace FFDownloader.Core.Hosts;

public interface IHostResolver
{
    string Host { get; }

    bool CanResolve(LinkCandidate link);

    Task<ResolvedDownload> ResolveAsync(LinkCandidate link, CancellationToken cancellationToken);
}
