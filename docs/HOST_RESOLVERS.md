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

`datanodes.to` is implemented with:

1. HTTP flow mirroring the site's own three-step protocol: GET the share link (redirects to `/download`, sets session cookies) and parse the hidden `#downloadForm` fields; `POST op=download1` with those fields to reveal the `<download-countdown>` component; `POST op=download2` (multipart) with the fields advertised by that component (`code`, `rand`, `referer`, `free-method`, `premium-method`) to get back `{"url": "..."}` — a percent-encoded direct link on a `*.dlproxy.uk` tunnel host.
2. Each resolve uses its own `HttpClientHandler`/`CookieContainer` (not a shared instance) because the site tracks the active file via a session cookie rather than the URL path, so concurrent resolves of sibling parts must not share cookies.
3. If the countdown component reports `has-captcha` or `has-password`, or if the expected markup/JSON isn't found, the resolver throws `ResolveRequiresBrowserException` and the hidden WebView2 fallback takes over: it clicks the "Continue to Download" button, lets the page's own JS run the countdown and invisible reCAPTCHA v3, and captures the final URL via `DownloadStarting`/`NavigationStarting` — the same generic pattern used for FuckingFast.
4. Advertised file size is parsed from the `data-scan-size` attribute on the initial page so multi-connection downloads can start immediately instead of waiting on a first response header.

`mediafire.com` is implemented with:

1. A single HTTP GET of the share page: the direct download URL is already embedded server-side in the `<a id="downloadButton" href="...">` element (no wait/AJAX step needed for the common case), and the advertised size is parsed from the adjacent "Download (50.27MB)" text.
2. Hidden WebView2 fallback for cases the button isn't present in the static HTML (password-protected files, interstitial pages, markup changes) — it polls the live document for the same button/anchor pattern used by the HTTP resolver.

Note: advertised sizes parsed from host pages (FuckingFast's "500.0MB", Datanodes' "500.0 MB", MediaFire's "50.27MB") are rounded display values, not exact byte counts. The download engine (`HttpDownloadService`) treats them only as a *planning* estimate for segment boundaries; the last segment's request is always open-ended (no upper `Range` bound) and the server's own `Content-Range` response is what's used for final size validation and progress display. This avoids both false "size mismatch" failures and silent truncation when a host's displayed size doesn't exactly match the real byte count.

`multiup.io` (and the legacy `multiup.org`, normalized to the same host) is a **mirror aggregator**, not a file host: its share page never serves file bytes itself, it lists the same file mirrored across several third-party hosts (gofile.io, 1fichier.com, mixdrop.ag, krakenfiles.com, etc. — the exact set varies per file). `MultiUpResolver` is a meta-resolver:

1. It POSTs the page's CSRF-protected mirror-list form (`_csrf_token` + the form's `action`) to fetch the list of `(host, url)` mirrors.
2. It walks the list and delegates to whichever *already-registered* resolver first reports `CanResolve(...) == true` for a mirror — reusing that resolver's full HTTP/WebView2 behavior unchanged. The original MultiUp link's filename/package/part metadata is preserved on the delegated `LinkCandidate` (mirror URLs rarely embed a friendly filename).
3. If none of the listed mirrors match a supported host, it throws with the exact list of (unsupported) hosts offered, so the failure is actionable instead of a generic error.

This means MultiUp support automatically grows as more direct host resolvers are added — no per-mirror-host code is needed here. There is no dedicated WebView2 fallback for the MultiUp meta-step itself (the mirror-list page is plain server-rendered HTML with no JS-derived tokens observed); once a supported mirror is found, that mirror's own resolver (including its browser fallback) takes over normally.

## Generic Mirror Hosts (`GenericBrowserMirrorResolver`)

MultiUp mirrors the same file across ~30 third-party hosts (see `DownloadLinkParser.GenericMirrorHosts`: gofile.io, krakenfiles.com, megaup.net, ranoz.gg, mixdrop.ag, ddownload.com, 1fichier.com, rapidgator.net, nitroflare.com, turbobit.net, 4shared.com, and more), each running a *different* platform with its own reveal/countdown/challenge mechanism — there is no shared template like FuckingFast/Datanodes/MediaFire have. Rather than writing and maintaining ~30 bespoke parsers, `GenericBrowserMirrorResolver` (App layer, since it needs WebView2) drives a real browser generically:

1. **Hidden attempt** (≤20s, no user interaction): loads the page off-screen, and on each poll either detects a captcha widget (`.cf-turnstile`, `.g-recaptcha`, `.h-captcha`, or a `recaptcha`/`hcaptcha`/`turnstile`/`captcha` iframe — confirmed against KrakenFiles' real Cloudflare Turnstile widget, detected within 1 second) or looks for an unclicked `<a>`/`<button>` whose text matches `/download/i` and isn't an ad/login/register link, and clicks it. The resulting file download is captured via the same `DownloadStarting`/`Content-Disposition` mechanism used by the other WebView2 resolvers.
2. **Interactive escalation**: if a captcha is detected, or the hidden attempt times out (e.g. MegaUp's CDN sits behind a Cloudflare Managed Challenge that a hidden, human-less browser session cannot pass — confirmed by reproduction), a *visible* window (`InteractiveBrowserResolverWindow`) opens showing the real page. The user completes whatever the site needs — solve the captcha, click through an ad, click Download — and the same capture mechanism finishes the resolve automatically once a real file download starts. This is the JDownloader-style "solve the captcha in a prompt" flow.

Because each mirrored host is genuinely a different site, this approach trades a lower *fully-automatic* success rate for much broader coverage without an unbounded number of bespoke resolvers — and it degrades gracefully to "ask the user" instead of failing outright. Hosts that need a fundamentally different protocol (mega.nz's own crypto scheme, Google Drive/Dropbox/OneDrive's OAuth-gated APIs, FTP) are intentionally excluded and still fail with a clear "unsupported host" message via MultiUp.

## Adding a Host

1. Add a resolver implementing `IHostResolver`.
2. Add parser tests using synthetic HTML.
3. Register the resolver in the app resolver registry.
4. Add or update `HostDownloadRules` if the host needs custom connection limits.

External plugin assembly loading is planned, but the internal contract is already separated so hosts can be moved out later without redesigning the download engine.

