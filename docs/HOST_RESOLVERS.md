# Host Resolvers

Host support is isolated behind `IHostResolver`.

```csharp
public interface IHostResolver
{
    string Host { get; }
    bool CanResolve(LinkCandidate link);
    Task<ResolvedDownload> ResolveAsync(LinkCandidate link, CancellationToken cancellationToken);
}
```

Resolvers convert a public file page into a direct downloadable URL. They should avoid UI prompts and should fail with actionable exceptions when a host cannot be resolved automatically.

## Current Resolver

`fuckingfast.co` is implemented with:

1. HTTP HTML parsing for fast paths.
2. Hidden WebView2 fallback for JavaScript/Cloudflare-gated pages.
3. URL extraction from `window.open`, anchors, onclick handlers and navigation events.

## Adding a Host

1. Add a resolver implementing `IHostResolver`.
2. Add parser tests using synthetic HTML.
3. Register the resolver in the app resolver registry.
4. Add or update `HostDownloadRules` if the host needs custom connection limits.

External plugin assembly loading is planned, but the internal contract is already separated so hosts can be moved out later without redesigning the download engine.

