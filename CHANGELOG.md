# Changelog

All notable changes to FFDOWNLOADER will be documented in this file.

## Unreleased

### Added

- Generic mirror host support (`GenericBrowserMirrorResolver`) covering ~30 additional MultiUp-mirrored hosts (gofile.io, krakenfiles.com, megaup.net, ranoz.gg, mixdrop.ag, 1fichier.com, rapidgator.net, nitroflare.com, turbobit.net, and more) that don't share a common platform: a hidden WebView2 attempt clicks through simple "Download" flows automatically, escalating to a **visible, interactive browser window** (JDownloader-style) when a captcha is detected or the hidden attempt times out, so the user can solve the captcha or click through manually and the download is captured automatically once it starts.
- Torrent support: a "Torrents" tab backed by MonoTorrent, accepting magnet links (pasted, one or more at a time) and `.torrent` files (browsed and copied into app-managed storage). Supports DHT, Peer Exchange, Local Peer Discovery, UPnP/NAT-PMP port forwarding, per-engine download/upload speed limits, a configurable listen port, and optional seed ratio/time limits enforced by the app (MonoTorrent has no built-in cap). Torrents persist across restarts and resume with their prior paused/running state.
- MultiUp.io (and legacy MultiUp.org) host support: a meta-resolver that fetches the site's mirror list and delegates to whichever already-supported host resolver matches one of the listed mirrors, preserving the original filename/package metadata. Fails with the exact list of unsupported mirror hosts when none match.
- MediaFire host support: automatic resolver extracting the direct download link already embedded in the share page, plus a hidden WebView2 fallback for password-protected or interstitial pages.
- Datanodes.to host support: automatic resolver speaking the site's `op=download1` / `op=download2` protocol directly (parsing the hidden form and the `<download-countdown>` component), plus a hidden WebView2 fallback for files that require captcha or a password.
- Classic download-manager interface inspired by JDownloader and IDM, with menu bar, toolbar, download categories, queue metrics, overview panel, and settings tabs.
- Expandable package rows in the download list, showing each file in the package with host, status, downloaded size, total size, speed, progress, and error details inline.
- App-level tests covering startup package selection and package row expansion state.
- Repository screenshots for the main queue and expanded package view.

### Fixed

- Multi-connection downloads no longer fail with a false "size mismatch" (or silently truncate the file) when a host's advertised size is a rounded display value instead of the exact byte count. The last segment's request is now open-ended and the server's own `Content-Range` is used as the authoritative total for validation and progress.
- Disposing the torrent engine (e.g. on app close) no longer risks a UI-thread deadlock: the async shutdown now runs off the calling thread instead of blocking synchronously on the WPF dispatcher's own synchronization context.
- Startup no longer crashes when a saved queue selects the first package before commands are initialized.
- FuckingFast resolver updated for the new site layout: the DOWNLOAD button now issues an htmx `POST /f/{id}/go` and the direct URL is returned in the `HX-Redirect` response header. The HTTP resolver speaks this protocol directly, and the hidden WebView2 fallback performs the same POST from inside the page (bypassing the Cloudflare TLS-fingerprint challenge that blocks plain HttpClient) instead of clicking the old `<button>` element that no longer exists.

## 1.0.0 - 2026-06-27

### Added

- Initial WPF/.NET 8 portable application.
- Clipboard monitoring and bulk link paste dialog.
- Multipart archive grouping.
- FuckingFast automatic resolver with hidden WebView2 fallback.
- Resumable HTTP downloads.
- Multi-connection segmented download engine.
- Temporary `.ffdownload` files with atomic final rename.
- Queue persistence and restart recovery.
- Expired URL retry/renewal flow.
- Per-host connection rules.
- Download speed limit and concurrency settings.
- Automatic archive extraction support.
- Core xUnit test suite.
