# Architecture

FFDOWNLOADER is split into a UI project and a Core project.

## Projects

- `FFDownloader.Core`: link parsing, package grouping, host resolution contracts, download queue, queue persistence, download engine, extraction, and the torrent engine wrapper (`Torrents/`).
- `FFDownloader.App`: WPF shell, commands, clipboard monitor, dialogs, settings storage and host resolver integrations that require UI technologies such as WebView2.

## Download Pipeline

1. Text is parsed into `LinkCandidate` objects.
2. Links are grouped into `DownloadPackageJob` instances.
3. The app resolves each link through `IHostResolver`.
4. `HttpDownloadService` downloads the resolved URL.
5. Progress is reported back to the WPF view models.
6. Queue state is persisted to `data/queue.json`.
7. Optional extraction runs after all parts in a package complete.

## Resumable Download Engine

The engine supports:

- Single-stream resume using HTTP `Range`.
- Multi-segment downloads using parallel range requests.
- Per-host connection rules through `HostDownloadRules`.
- Temporary `.ffdownload` files.
- Segment files named `.ffdownload.segNNN`.
- Metadata files named `.ffdownload.state`.
- ETag and Last-Modified validation when resuming.
- Atomic rename to the final filename after validation.

If a server ignores range requests, the engine falls back to a normal single-stream download.

## Host Resolution

Supported hosts: `fuckingfast.co`, `datanodes.to`, `mediafire.com`, `multiup.io`/`multiup.org` (a meta-resolver that delegates to one of the others), and ~30 generic mirror hosts handled by one shared browser-driven resolver — see [Host Resolvers](HOST_RESOLVERS.md) for the full breakdown.

General resolution strategy per host:

1. Fast HTTP parse of the page HTML (each host speaks its own protocol — htmx POST, `download1`/`download2`, an embedded direct link, or a mirror-list form).
2. Hidden WebView2 fallback when Cloudflare/JavaScript blocks the HTTP path. Extraction of the final download URL from `window.open`, anchors, button handlers or observed navigation events.
3. For hosts with no dedicated parser (the generic mirror group), the hidden WebView2 attempt clicks through automatically; if it detects a captcha or times out, it escalates to a **visible** WebView2 window so the user can complete the step manually (solve the captcha, click through an ad) — the download is still captured automatically once it starts. This is the only case where the user is expected to interact with a resolver window.

## Torrent Pipeline

Torrents are a separate, parallel pipeline from the HTTP download queue — magnet links and `.torrent` files are not `LinkCandidate`s and don't go through `IHostResolver`.

1. `TorrentSourceParser` extracts magnet URIs from pasted text; `.torrent` files are added via a file picker and copied into `data/torrents/`.
2. `TorrentEngineService` wraps a single `MonoTorrent.Client.ClientEngine` for the app's lifetime, translating `TorrentClientSettings` into MonoTorrent's `EngineSettings`/`TorrentSettings`.
3. `TorrentsViewModel` (App) owns the `ObservableCollection<TorrentItemViewModel>`, persists the list of added torrents to `data/torrent-queue.json` via `TorrentQueueStore`, and polls each `TorrentManager` on a 1-second `DispatcherTimer` tick to refresh bindable progress/speed/peer properties (MonoTorrent doesn't raise property-changed notifications itself).
4. Per-torrent resume state (pieces already verified, in-flight blocks), DHT node cache, and magnet metadata are cached by MonoTorrent itself under `data/torrent-cache/` (`AutoSaveLoadFastResume`/`AutoSaveLoadDhtCache`/`AutoSaveLoadMagnetLinkMetadata`); FFDOWNLOADER only tracks *which* torrents exist and their paused/running intent.
5. Seed ratio/time limits are not a MonoTorrent feature — `TorrentEngineService.EnforceSeedLimitsAsync` is called from the same refresh tick and stops any torrent that has exceeded the configured ratio or time while seeding.

