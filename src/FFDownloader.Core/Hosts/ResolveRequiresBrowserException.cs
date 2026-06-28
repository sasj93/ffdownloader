namespace FFDownloader.Core.Hosts;

public sealed class ResolveRequiresBrowserException : Exception
{
    public ResolveRequiresBrowserException(string message)
        : base(message)
    {
    }
}
