param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$')]
    [string] $Version,

    [string] $Repository = 'sasj93/ffdownloader'
)

$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$tag = "v$Version"
$zip = Join-Path $root "artifacts\release\FFDOWNLOADER-win-x64-$tag.zip"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI is required. Install it with: winget install --id GitHub.cli -e'
}

gh auth status | Out-Null

& (Join-Path $PSScriptRoot 'package-release.ps1') -Version $Version

git tag -f $tag
git push origin $tag --force

gh release view $tag --repo $Repository *> $null
if ($LASTEXITCODE -eq 0) {
    gh release upload $tag $zip --repo $Repository --clobber
} else {
    gh release create $tag $zip `
        --repo $Repository `
        --title "FFDOWNLOADER $tag" `
        --notes-file (Join-Path $root 'CHANGELOG.md')
}

Write-Host "Published release $tag to $Repository"

