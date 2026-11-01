# Builds the publish folder for GitHub Pages and for preview.ps1.
# Usage:  pwsh stage.ps1 -Out <dir>
param(
    [Parameter(Mandatory = $true)]
    [string]$Out
)

$ErrorActionPreference = 'Stop'
$Site = $PSScriptRoot
$Repo = Split-Path $Site -Parent
$OutFull = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path (Get-Location) $Out }

if (Test-Path $OutFull) { Remove-Item -Recurse -Force $OutFull }
New-Item -ItemType Directory -Path $OutFull | Out-Null

$pages = @('index.html', 'styles.css', 'site.js', 'favicon.png')
foreach ($f in $pages) {
    $src = Join-Path $Site $f
    if (-not (Test-Path $src)) { throw "Missing page file: $src" }
    Copy-Item $src (Join-Path $OutFull $f)
}

# Screenshots and animations from docs/ (top level only).
$imagesOut = Join-Path $OutFull 'images'
New-Item -ItemType Directory -Path $imagesOut | Out-Null
Get-ChildItem -LiteralPath (Join-Path $Repo 'docs') -File |
    Where-Object { $_.Extension -in '.png', '.gif' } |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $imagesOut $_.Name) }

# Tells GitHub Pages not to run Jekyll on the output.
New-Item -ItemType File -Path (Join-Path $OutFull '.nojekyll') | Out-Null

Write-Host "Staged -> $OutFull"
