# Builds the self-contained executable, then the installer: publish\installer\DalviiioDDRLauncher-Setup-<version>.exe
# Usage (from the repository root): powershell -File installer\build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

[xml]$project = Get-Content (Join-Path $root 'src\DdrLauncher.App\DdrLauncher.App.csproj')
$version = @($project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw 'No <Version> in DdrLauncher.App.csproj.' }

dotnet publish (Join-Path $root 'src\DdrLauncher.App') -p:PublishProfile=SelfContained
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found.' }

& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'DalviiioDDRLauncher.iss')
if ($LASTEXITCODE -ne 0) { throw 'The installer could not be built.' }

Get-Item (Join-Path $root "publish\installer\DalviiioDDRLauncher-Setup-$version.exe")
