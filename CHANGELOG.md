# Changelog

All notable changes to FFDOWNLOADER will be documented in this file.

## Unreleased

### Added

- Classic download-manager interface inspired by JDownloader and IDM, with menu bar, toolbar, download categories, queue metrics, overview panel, and settings tabs.
- Expandable package rows in the download list, showing each file in the package with host, status, downloaded size, total size, speed, progress, and error details inline.
- App-level tests covering startup package selection and package row expansion state.
- Repository screenshots for the main queue and expanded package view.

### Fixed

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
