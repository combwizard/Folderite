# Stage the site, build the docs into it and serve it locally. Ctrl+C stops the server.
# Usage:  powershell -ExecutionPolicy Bypass -File preview.ps1 [-Port 8000]
param(
    [int]$Port = 8000
)

$ErrorActionPreference = 'Stop'
$Site = $PSScriptRoot
$Repo = Split-Path $Site -Parent
$Preview = Join-Path $Site '.preview'

# Refuse if the port is already taken.
$taken = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($taken) {
    throw "Port $Port is already in use. Stop the other server, or pass -Port <n>."
}

& (Join-Path $Site 'stage.ps1') -Out $Preview

# The docs go under /docs/, as on the published site. Needs: pip install -r requirements-docs.txt
$env:NO_MKDOCS_2_WARNING = '1'
python -m mkdocs build --strict --quiet -f (Join-Path $Repo 'mkdocs.yml') -d (Join-Path $Preview 'docs')
if ($LASTEXITCODE -ne 0) { throw 'mkdocs build failed.' }

Write-Host "Serving http://127.0.0.1:$Port/  (Ctrl+C to stop)"
python -m http.server $Port --bind 127.0.0.1 --directory $Preview
