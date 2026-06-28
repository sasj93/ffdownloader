using FFDownloader.Core.Links;

namespace FFDownloader.Core.Hosts;

public sealed class ResolverRegistry
{
    private readonly IReadOnlyList<IHostResolver> _resolvers;

    public ResolverRegistry(IEnumerable<IHostResolver> resolvers)
    {
        _resolvers = resolvers.ToList();
    }

    public IHostResolver? FindResolver(LinkCandidate link)
    {
        return _resolvers.FirstOrDefault(resolver => resolver.CanResolve(link));
    }
}
