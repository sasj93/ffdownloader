# FFDOWNLOADER

FFDOWNLOADER is a portable Windows download manager built with .NET 8 and WPF. It monitors the clipboard, captures supported file-host links, groups multipart archives automatically, resolves final download URLs, and downloads files with resumable multi-connection transfers.

> Use FFDOWNLOADER only for files you are authorized to download. Host support is implemented as plugins/adapters and may need maintenance when a website changes its markup or protection flow.

## Highlights

- Portable single-file Windows executable.
- Clipboard monitor with confirmation before adding links.
- Bulk paste dialog that extracts links from messy text.
- Automatic grouping of multipart archives such as `.part001.rar`, `.part002.rar`, etc.
- Automatic FuckingFast resolver with fast HTTP parsing and hidden WebView2 fallback.
- Resumable downloads using HTTP `Range`.
- Multi-connection segmented downloads per file.
- Adaptive connection rules by host.
- Temporary `.ffdownload` files and atomic commit when complete.
- Queue persistence in `data/queue.json`.
- Automatic retry and expired `/dl/` URL renewal.
- Speed limit, concurrent downloads, connection count, extraction and password settings.
- Automatic extraction for supported archives through SharpCompress.
- Classic download-manager UI inspired by JDownloader and IDM.
- Expandable package rows with per-file status, progress, speed and errors inline.

## Screenshots

![FFDOWNLOADER main download queue](docs/assets/screenshots/ffdownloader-main.png)

![Expanded package with per-file progress](docs/assets/screenshots/ffdownloader-expanded-package.png)

## Download

The recommended build is published as a GitHub Release:

- `FFDOWNLOADER-win-x64.zip`

After extracting, run:

```powershell
FFDOWNLOADER.exe
```

The application stores portable runtime state next to the executable under `data/`.

## Build From Source

Requirements:

- Windows 10 or newer
- .NET 8 SDK
- Microsoft Edge WebView2 Runtime

Build and test:

```powershell
dotnet test FFDownloader.sln
dotnet build src\FFDownloader.App\FFDownloader.App.csproj -c Release
```

Publish the portable executable:

```powershell
.\scripts\package-release.ps1 -Version 1.0.0
```

The release ZIP is written to `artifacts\release\`.

## Repository Layout

```text
src/
  FFDownloader.Core/     Download engine, queue, parsers, host resolvers
  FFDownloader.App/      WPF application
tests/
  FFDownloader.Core.Tests/
  FFDownloader.App.Tests/
docs/
  assets/screenshots/
  ARCHITECTURE.md
  DOWNLOAD_ENGINE.md
  HOST_RESOLVERS.md
  RELEASING.md
scripts/
  package-release.ps1
  publish-release.ps1
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Download engine](docs/DOWNLOAD_ENGINE.md)
- [Host resolvers](docs/HOST_RESOLVERS.md)
- [Usage](docs/USAGE.md)
- [Releasing](docs/RELEASING.md)
- [Roadmap](docs/ROADMAP.md)

## Current Host Support

| Host | Status | Notes |
| --- | --- | --- |
| `fuckingfast.co` | Supported | HTTP parser plus hidden WebView2 fallback for button-based `/dl/` resolution. |

## Roadmap

- Mirror support for the same file across multiple hosts.
- Pluggable host resolver discovery from external assemblies.
- Per-host UI profiles.
- Optional checksum verification when hosts expose hashes.
- More archive diagnostics before extraction.

## License

MIT. See [LICENSE](LICENSE).
