# Build tuigether (Release, single-file, framework-dependent) and install onto PATH.
# Usage: scripts\install.ps1 [-InstallDir <dir>]
# Default dir: %LOCALAPPDATA%\Programs\tuigether. Linux/macOS: use install.sh.
#Requires -Version 5
[CmdletBinding()]
param(
  [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\tuigether')
)
$ErrorActionPreference = 'Stop'

# Script lives in scripts\, the project lives in src\ next to it.
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'src'

$arch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
$rid = "win-$arch"

Write-Host "Publishing tuigether ($rid)..."
dotnet publish $project -c Release -r $rid --self-contained false -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$binary = Join-Path $project "bin\Release\net10.0\$rid\publish\Tuigether.exe"
if (-not (Test-Path $binary)) { throw "Publish succeeded but binary not found at $binary" }

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$target = Join-Path $InstallDir 'tuigether.exe'
Copy-Item $binary $target -Force
Write-Host "Installed: $target"

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (($userPath -split ';') -notcontains $InstallDir) {
  Write-Host "Note: $InstallDir is not on your PATH. Add it for this user with:"
  Write-Host "  setx PATH `"$InstallDir;`$($env:Path)`""
}
