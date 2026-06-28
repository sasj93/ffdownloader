# Contributing

## Development Setup

1. Install the .NET 8 SDK.
2. Clone the repository.
3. Run the test suite:

```powershell
dotnet test tests\FFDownloader.Core.Tests\FFDownloader.Core.Tests.csproj
```

## Code Style

- Keep host-specific behavior behind resolver or rule classes.
- Keep the Core project UI-free and testable.
- Add tests for download behavior, queue persistence, parsing and resolver changes.
- Avoid committing runtime state, generated binaries, `.ffdownload` files, `bin/`, `obj/`, or local SDK folders.

## Pull Requests

Include:

- What changed.
- Why it changed.
- How it was tested.
- Any host-specific behavior that may need manual validation.

