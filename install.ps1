<#
.SYNOPSIS
    Install UIA CLI — Windows UI Automation tool for AI agents.
.DESCRIPTION
    Downloads the latest release from GitHub, extracts to %LOCALAPPDATA%\Programs\uiacli,
    and adds it to the user PATH. No .NET SDK or Go toolchain required.
.EXAMPLE
    irm https://raw.githubusercontent.com/amitse/uiacli/master/install.ps1 | iex
#>

$ErrorActionPreference = "Stop"

$repo = "amitse/uiacli"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\uiacli"

Write-Host ""
Write-Host "  UIA CLI Installer" -ForegroundColor Cyan
Write-Host "  =================" -ForegroundColor Cyan
Write-Host ""

# Get latest release info
Write-Host "  Finding latest release..." -ForegroundColor Gray
$release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest"
$version = $release.tag_name
$asset = $release.assets | Where-Object { $_.name -like "*win-x64.zip" } | Select-Object -First 1

if (-not $asset) {
    Write-Host "  ERROR: No win-x64 asset found in release $version" -ForegroundColor Red
    exit 1
}

Write-Host "  Latest version: $version" -ForegroundColor Green
Write-Host "  Asset: $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB)" -ForegroundColor Gray

# Download
$zipPath = Join-Path $env:TEMP "uiacli-$version.zip"
Write-Host "  Downloading..." -ForegroundColor Gray
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -UseBasicParsing

# Extract
if (Test-Path $installDir) {
    Write-Host "  Removing previous installation..." -ForegroundColor Gray
    Remove-Item -Recurse -Force $installDir
}
Write-Host "  Extracting to $installDir..." -ForegroundColor Gray
Expand-Archive -Path $zipPath -DestinationPath $installDir -Force
Remove-Item $zipPath -Force

# Verify
$uiaExe = Join-Path $installDir "uia.exe"
if (-not (Test-Path $uiaExe)) {
    Write-Host "  ERROR: uia.exe not found after extraction" -ForegroundColor Red
    exit 1
}

# Add to PATH if not already there
$userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($userPath -notlike "*$installDir*") {
    Write-Host "  Adding to PATH..." -ForegroundColor Gray
    [Environment]::SetEnvironmentVariable("PATH", "$installDir;$userPath", "User")
    $env:PATH = "$installDir;$env:PATH"
}

Write-Host ""
Write-Host "  Installed UIA CLI $version" -ForegroundColor Green
Write-Host "  Location: $installDir" -ForegroundColor Gray
Write-Host ""
Write-Host "  Try it:" -ForegroundColor Cyan
Write-Host "    uia windows          # list all open windows" -ForegroundColor White
Write-Host "    uia tree Calculator  # inspect Calculator's UI" -ForegroundColor White
Write-Host ""
Write-Host "  Restart your terminal for PATH changes to take effect." -ForegroundColor Yellow
Write-Host ""
