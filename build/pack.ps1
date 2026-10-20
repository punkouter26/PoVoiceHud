# Builds the installer and the update packages with Velopack (same version as the Velopack NuGet package, pinned in
# .config/dotnet-tools.json). The version comes from <Version> in src/PoVoiceHud.App/PoVoiceHud.App.csproj: raise it first.
#
#   pwsh build/pack.ps1                                   # → artifacts/releases/PoVoiceHud.App-win-Setup.exe and the update files
#   pwsh build/pack.ps1 -Feed \\server\share\PoVoiceHud   # …and copies them to the folder that UpdateFeed in settings.json names
#
# Keep artifacts/releases between versions: Velopack builds the small delta updates from the previous release in it.
param([string]$Feed)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/PoVoiceHud.App/PoVoiceHud.App.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $project" }
$publish = Join-Path $root 'artifacts/publish'
$releases = Join-Path $root 'artifacts/releases'

dotnet tool restore
if ($LASTEXITCODE) { throw 'dotnet tool restore failed' }
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

# The pack id differs from the data folder's name (%LOCALAPPDATA%\PoVoiceHud) on purpose: uninstalling removes the install
# folder, and settings, models and session logs must survive that.
dotnet vpk pack --packId PoVoiceHud.App --packVersion $version --packTitle PoVoiceHud --packAuthors PoVoiceHud `
    --packDir $publish --mainExe PoVoiceHud.App.exe --icon (Join-Path $root 'src/PoVoiceHud.App/Assets/PoVoiceHud.ico') `
    --runtime win-x64 --shortcuts StartMenuRoot --outputDir $releases
if ($LASTEXITCODE) { throw 'vpk pack failed' }

if ($Feed) {
    New-Item -ItemType Directory -Force $Feed | Out-Null
    Copy-Item (Join-Path $releases '*') $Feed -Force
    Write-Host "Copied the release to $Feed"
}
Write-Host "PoVoiceHud $version → $releases"
