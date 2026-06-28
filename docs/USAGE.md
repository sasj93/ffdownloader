# Usage

## Adding Links

FFDOWNLOADER can add links in two ways:

- Clipboard monitor: copy one or more supported links and confirm the capture prompt.
- `+ Links`: paste arbitrary text; the parser extracts supported links automatically.

## Starting Downloads

Press `Start`. The app resolves each link, downloads files, and persists progress. If the app closes, reopen it and press `Start` again to continue queued or paused items.

## Important Settings

- `Downloads simultaneos`: maximum number of files being downloaded at once.
- `Conexoes por arquivo`: maximum number of range segments per file.
- `Tamanho minimo para multi-conexao`: small files use a single stream to avoid overhead.
- `Limite de velocidade`: global download speed cap in KB/s.
- `Renovar URL expirada automaticamente`: re-resolves the original page if a final `/dl/` URL expires.
- `Validar identidade remota ao retomar`: compares ETag and Last-Modified when available before appending bytes.

## Runtime Files

During downloads, files may appear as:

- `file.rar.ffdownload`
- `file.rar.ffdownload.state`
- `file.rar.ffdownload.seg000`

These are expected. They are removed or committed when the download completes.

