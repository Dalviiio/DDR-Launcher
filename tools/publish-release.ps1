# Publishes a release of Dalviiio DDR Launcher on GitHub.
#   1. builds the installer (installer\build.ps1)
#   2. signs it with the author's private key (tools\ReleaseSigner, key outside the repository)
#   3. creates the GitHub release vX.Y.Z (or vX.Y.Z-beta with -Beta) with the installer and its signature
# Installed launchers find it, check the signature and offer the update.
# Usage: powershell -File tools\publish-release.ps1 -Notes "What changed" [-Beta]
param([string]$Notes = "", [switch]$Beta)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repository = 'Dalviiio/DDR-Launcher'
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI (gh) is needed to publish: https://cli.github.com' }

$installer = & (Join-Path $root 'installer\build.ps1') | Select-Object -Last 1
if (-not $installer) { throw 'The installer was not built.' }
$version = $installer.Name -replace '^DalviiioDDRLauncher-Setup-(.+)\.exe$', '$1'

dotnet run --project (Join-Path $root 'tools\ReleaseSigner') -- sign $installer.FullName
if ($LASTEXITCODE -ne 0) { throw 'The installer could not be signed.' }
$signature = "$($installer.FullName).sig"

$tag = if ($Beta) { "v$version-beta" } else { "v$version" }
$title = if ($Beta) { "Dalviiio DDR Launcher $version beta" } else { "Dalviiio DDR Launcher $version" }
$extra = if ($Beta) { @('--prerelease') } else { @() }
gh release create $tag $installer.FullName $signature --repo $repository --title $title --notes $Notes @extra
if ($LASTEXITCODE -ne 0) { throw 'The GitHub release could not be created.' }
Write-Output "Published $title on https://github.com/$repository/releases"
