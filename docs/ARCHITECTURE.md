# Architecture

FFDOWNLOADER is split into a UI project and a Core project.

## Projects

- `FFDownloader.Core`: link parsing, package grouping, host resolution contracts, download queue, queue persistence, download engine and extraction.
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

The initial supported host is `fuckingfast.co`.

Resolution strategy:

1. Fast HTTP parse of the page HTML.
2. Hidden WebView2 fallback when Cloudflare/JavaScript blocks the HTTP path.
3. Extraction of the final `/dl/` URL from `window.open`, anchors, button handlers or observed navigation events.

The user should not need to interact with the resolver window.

