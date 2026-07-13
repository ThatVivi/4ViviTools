param(
    [switch]$Dev,
    [switch]$DownloadOnly
)

$ErrorActionPreference = "Stop"

$channel = if ($Dev) { "main" } else { "stable" }
$scriptUrl = "https://alia5.github.io/VIIPER/$channel/install.ps1"
$cache = Join-Path $PSScriptRoot "cache"
$scriptPath = Join-Path $cache "viiper-$channel-install.ps1"

New-Item -ItemType Directory -Force -Path $cache | Out-Null
Write-Host "Downloading official VIIPER $channel installer script..."
Invoke-WebRequest -Uri $scriptUrl -OutFile $scriptPath -UseBasicParsing
Write-Host "Saved to: $scriptPath"

if ($DownloadOnly) {
    Write-Host "DownloadOnly was set. Review the script, then run it manually if desired."
    return
}

Write-Host "Running official VIIPER installer. This is user-triggered; 4ViviTools does not run it on startup."
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath

$viiperExe = Join-Path $env:LOCALAPPDATA "VIIPER\viiper.exe"
if (Test-Path -LiteralPath $viiperExe) {
    Write-Host "VIIPER installed: $viiperExe" -ForegroundColor Green
} else {
    Write-Warning "VIIPER executable was not found at $viiperExe after installer finished."
}
