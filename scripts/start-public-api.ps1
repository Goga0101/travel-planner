# Exposes the local API (http://localhost:5080) through a Cloudflare quick tunnel so the GitHub Pages site can
# reach it over HTTPS (browsers block public HTTPS pages from calling http://localhost).
# The quick-tunnel URL changes on every start, so this script writes it to
# frontend/travel-planner-web/gh-pages-api-url.txt and pushes it, which redeploys the site (about 1-2 minutes).
# Keep this window open: closing it stops the tunnel.
#
# Prerequisites: the API is running (dotnet run --project src/TravelPlanner.API --launch-profile http)
# and cloudflared.exe is in %LOCALAPPDATA%\Programs\cloudflared or on PATH.

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$urlFile = Join-Path $repo 'frontend/travel-planner-web/gh-pages-api-url.txt'

$cloudflared = Join-Path $env:LOCALAPPDATA 'Programs\cloudflared\cloudflared.exe'
if (-not (Test-Path $cloudflared)) { $cloudflared = (Get-Command cloudflared -ErrorAction Stop).Source }

try { Invoke-WebRequest 'http://localhost:5080/health/live' -UseBasicParsing -TimeoutSec 5 | Out-Null }
catch { throw 'The API is not running on http://localhost:5080. Start it first.' }

$log = Join-Path $env:TEMP 'travel-planner-tunnel.log'
if (Test-Path $log) { Remove-Item $log }
$tunnel = Start-Process $cloudflared -ArgumentList 'tunnel', '--no-autoupdate', '--url', 'http://localhost:5080' `
    -RedirectStandardError $log -NoNewWindow -PassThru

$url = $null
for ($i = 0; $i -lt 60 -and -not $url; $i++) {
    Start-Sleep -Seconds 1
    if (Test-Path $log) {
        $match = Select-String -Path $log -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' | Select-Object -First 1
        if ($match) { $url = $match.Matches[0].Value }
    }
}
if (-not $url) { Stop-Process $tunnel; throw "Tunnel did not start. See $log" }
Write-Host "Tunnel: $url"

$current = if (Test-Path $urlFile) { (Get-Content $urlFile -Raw).Trim() } else { '' }
if ($current -ne $url) {
    Set-Content -Path $urlFile -Value $url -Encoding ascii
    git -C $repo add -- $urlFile
    git -C $repo commit -m "Point GitHub Pages at current API tunnel" -- $urlFile
    git -C $repo push
    Write-Host 'Pushed. The site updates in about 1-2 minutes: https://goga0101.github.io/travel-planner/'
}

Write-Host 'Tunnel running. Press Ctrl+C to stop.'
Wait-Process -Id $tunnel.Id
