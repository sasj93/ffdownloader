# Releasing

Releases are versioned with SemVer tags such as `v1.0.0`.

## Local Package

```powershell
.\scripts\package-release.ps1 -Version 1.0.0
```

Output:

```text
artifacts\release\FFDOWNLOADER-win-x64-v1.0.0.zip
```

## GitHub Release

After authenticating with the GitHub CLI:

```powershell
gh auth login
.\scripts\publish-release.ps1 -Version 1.0.0
```

The script creates or updates tag `v1.0.0`, pushes it, and uploads the ZIP to the GitHub Release.

## CI Release

Pushing a tag matching `v*.*.*` also runs `.github/workflows/release.yml`.

