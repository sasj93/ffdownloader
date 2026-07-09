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

1. HTTP HTML parsing for fast paths (`window.open` and download anchors — legacy page layout).
2. htmx protocol support for the current page layout: the DOWNLOAD anchor carries `hx-post="/f/{id}/go"`; POSTing to that endpoint returns the direct `dl.fuckingfast.co/dl/…` URL in the `HX-Redirect` response header. The endpoint is parsed from the page or derived from the file id in the source URL.
3. Hidden WebView2 fallback for Cloudflare-gated pages. Cloudflare challenges the .NET `HttpClient` TLS fingerprint (`Cf-Mitigated: challenge`) even with browser-like headers, so a real Chromium is required to pass the challenge; the fallback then performs the htmx POST via `fetch` inside the page (avoiding ad popups triggered by clicking) and falls back to clicking the DOWNLOAD element (`<a>` or `<button>`) if needed.
4. URL extraction from `window.open`, anchors, onclick handlers and navigation events.

Note: only the main site is challenge-protected. The final `dl.fuckingfast.co/dl/…` URLs download fine through plain `HttpClient`, including ranged multi-connection requests.

## Adding a Host

1. Add a resolver implementing `IHostResolver`.
2. Add parser tests using synthetic HTML.
3. Register the resolver in the app resolver registry.
4. Add or update `HostDownloadRules` if the host needs custom connection limits.

External plugin assembly loading is planned, but the internal contract is already separated so hosts can be moved out later without redesigning the download engine.

