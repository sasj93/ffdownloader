# Download Engine

`HttpDownloadService` is the core transfer engine.

## Capabilities

- HTTP `Range` resume.
- Multi-segment downloads.
- Per-host connection rules.
- Global speed limiting.
- Temporary files with atomic final rename.
- ETag and Last-Modified validation.
- Size validation before completion.
- Fallback to single-stream download when the server ignores range requests.

## Runtime Files

| File | Meaning |
| --- | --- |
| `name.ext.ffdownload` | Temporary single-stream or merged file. |
| `name.ext.ffdownload.state` | Resume metadata. |
| `name.ext.ffdownload.seg000` | Segment file for multi-connection downloads. |

Only the final filename is left after a successful download.

## Why Temporary Files

The app never exposes a partially downloaded file as if it were complete. It writes to `.ffdownload`, validates size and metadata, then renames to the final path.

