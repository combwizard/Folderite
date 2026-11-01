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

# Cache busting: GitHub Pages lets browsers cache files for 10 minutes, so after a deploy a browser
# could combine the new page with an old site.js or an old screenshot. Every quoted reference to a
# staged file in index.html gets ?v=<content hash>, so a changed file always has a new URL.
$indexPath = Join-Path $OutFull 'index.html'
$html = [IO.File]::ReadAllText($indexPath)
$outRoot = [IO.Path]::GetFullPath($OutFull).TrimEnd('\', '/')
Get-ChildItem -LiteralPath $OutFull -File -Recurse | Where-Object Name -ne 'index.html' | ForEach-Object {
    $rel = $_.FullName.Substring($outRoot.Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.Substring(0, 10).ToLowerInvariant()
    $html = $html.Replace("`"$rel`"", "`"$rel`?v=$hash`"")
}
[IO.File]::WriteAllText($indexPath, $html, [Text.UTF8Encoding]::new($false))

# Tells GitHub Pages not to run Jekyll on the output.
New-Item -ItemType File -Path (Join-Path $OutFull '.nojekyll') | Out-Null

Write-Host "Staged -> $OutFull"
