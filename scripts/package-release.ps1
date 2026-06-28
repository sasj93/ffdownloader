param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$')]
    [string] $Version,

    [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dotnet = Join-Path $root '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $dotnet = 'dotnet'
}

$publishDir = Join-Path $root "artifacts\publish\FFDOWNLOADER-$Runtime"
$releaseDir = Join-Path $root 'artifacts\release'
$zipPath = Join-Path $releaseDir "FFDOWNLOADER-$Runtime-v$Version.zip"

Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $publishDir | Out-Null
New-Item -ItemType Directory -Force $releaseDir | Out-Null

& $dotnet test (Join-Path $root 'tests\FFDownloader.Core.Tests\FFDownloader.Core.Tests.csproj') --configuration Release
& $dotnet publish (Join-Path $root 'src\FFDownloader.App\FFDownloader.App.csproj') `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    -p:Version=$Version `
    -p:AssemblyVersion=$Version `
    -p:FileVersion=$Version `
    -o $publishDir

Get-ChildItem $publishDir -File |
    Where-Object { $_.Extension -in '.pdb', '.xml' } |
    Remove-Item -Force

Copy-Item (Join-Path $root 'README.md') $publishDir
Copy-Item (Join-Path $root 'LICENSE') $publishDir

Remove-Item -Force $zipPath -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force

Write-Host "Created $zipPath"

