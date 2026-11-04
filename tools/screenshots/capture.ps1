# Captures the README/website screenshots and animations in the CURRENT Windows theme and writes
# docs\images\<name>-light.* or docs\images\<name>-dark.*. Every capture is needed in both themes: switch Windows
# between light and dark mode (Settings > Personalization > Colors) and run this once in each.
#
# Takes over the mouse and keyboard for about a minute; don't touch them until it says "done".
# Before running: make sure Directory Opus isn't replacing Explorer. A running Folderite is quit
# for the capture and started again afterwards. Your clipboard is saved and put back, and so is the
# navigation pane setting of Explorer and file dialogs (hidden while capturing).
#
# Usage:  pwsh tools\screenshots\capture.ps1
# Needs:  .NET 10 SDK, ffmpeg on PATH, a free X: drive letter, a primary monitor of at least 1600x1080.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$work = Join-Path $here '.work'
$docs = Join-Path $repo 'docs\images'

$light = (Get-ItemPropertyValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'AppsUseLightTheme') -eq 1
$theme = if ($light) { 'light' } else { 'dark' }
$env:BACKDROP = if ($light) { '#D9E2EC' } else { '#1F242C' }   # site/styles.css --stage matches these
$env:APP = Join-Path $work 'app'
$env:OUT = Join-Path $work "out-$theme"
Write-Host "Theme: $theme"

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) { throw 'ffmpeg is not on PATH' }
New-Item -ItemType Directory -Force $work | Out-Null
if (Test-Path $env:OUT) { Remove-Item $env:OUT -Recurse -Force }
New-Item -ItemType Directory $env:OUT | Out-Null

$driverDir = Join-Path $work 'driver'
dotnet build (Join-Path $here 'driver\Driver.csproj') -c Release -o $driverDir -v q -nologo | Out-Null
$driver = Join-Path $driverDir 'Driver.exe'
if (-not (Test-Path $driver)) { throw 'driver build failed' }

function Invoke-Driver([string]$scenario) {
    & $driver $scenario
    if ($LASTEXITCODE -ne 0) { throw "driver failed on $scenario" }
}

$exitScenario = Join-Path $work 'exit.txt'
Set-Content $exitScenario 'ipc exit'
function Stop-Folderite {
    if (-not (Get-Process Folderite -ErrorAction SilentlyContinue)) { return }
    & $driver $exitScenario | Out-Null
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Process Folderite -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    Get-Process Folderite -ErrorAction SilentlyContinue | Stop-Process -Force
}

# The navigation pane would show your own pinned folders, drives and network places, so it is hidden
# in Explorer and in file dialogs for the capture. The exact previous values are put back afterwards
# (or removed again if they didn't exist). Layout: pane width, visible flag, 0, window width.
$explorerKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer'
$sizers = @("$explorerKey\Modules\GlobalSettings\Sizer", "$explorerKey\CIDSave\Modules\GlobalSettings\Sizer")
$sizerValue = 'PageSpaceControlSizer'
$savedSizers = @{}
function Hide-NavPanes {
    foreach ($k in $sizers) {
        $old = if (Test-Path $k) { (Get-ItemProperty $k -ErrorAction SilentlyContinue).$sizerValue }
        $savedSizers[$k] = $old
        [byte[]]$new = if ($old) { $old.Clone() } else { 0x95, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0xA0, 4, 0, 0 }
        $new[4] = 0
        if (-not (Test-Path $k)) { New-Item $k -Force | Out-Null }
        Set-ItemProperty $k $sizerValue $new -Type Binary
    }
}
function Restore-NavPanes {
    foreach ($k in $savedSizers.Keys) {
        if ($null -ne $savedSizers[$k]) { Set-ItemProperty $k $sizerValue $savedSizers[$k] -Type Binary }
        else { Remove-ItemProperty $k $sizerValue -ErrorAction SilentlyContinue }
    }
}

& (Join-Path $here 'setup.ps1')

# Quit whatever Folderite is running (remember it to restart it), then start the demo copy.
$restart = @(Get-Process Folderite -ErrorAction SilentlyContinue | ForEach-Object Path | Select-Object -Unique)
Stop-Folderite
try {
    Hide-NavPanes
    Start-Process (Join-Path $env:APP 'Folderite.exe')
    Start-Sleep -Seconds 2
    Invoke-Driver (Join-Path $here 'scenario.txt')
}
finally {
    Restore-NavPanes
    Stop-Folderite
    subst X: /D 2>$null | Out-Null
    foreach ($p in $restart) { if ($p -and (Test-Path $p)) { Start-Process $p } }
}

# Recordings -> GIFs (palette per clip, only changed rectangles redrawn).
foreach ($name in 'explorer', 'save-dialog') {
    $mkv = Join-Path $env:OUT "$name.mkv"
    $gif = Join-Path $env:OUT "$name.gif"
    ffmpeg -hide_banner -loglevel error -y -i $mkv -vf 'fps=25,split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=none:diff_mode=rectangle' $gif
    if ($LASTEXITCODE -ne 0) { throw "gif conversion failed for $name" }

    # Reduced-motion still: the frame at the scenario's "mark" for this clip.
    $at = (Get-Content (Join-Path $env:OUT "$name-still.t") -Raw).Trim()
    ffmpeg -hide_banner -loglevel error -y -ss $at -i $mkv -frames:v 1 (Join-Path $env:OUT "$name-still.png")
    if ($LASTEXITCODE -ne 0) { throw "still extraction failed for $name" }
}

foreach ($f in 'menu.png', 'settings.png', 'explorer.gif', 'save-dialog.gif', 'explorer-still.png', 'save-dialog-still.png') {
    $src = Join-Path $env:OUT $f
    if (-not (Test-Path $src)) { throw "missing capture: $f" }
    $dst = Join-Path $docs ([IO.Path]::GetFileNameWithoutExtension($f) + "-$theme" + [IO.Path]::GetExtension($f))
    Copy-Item $src $dst -Force
    Write-Host ("{0,-28} {1,8:N0} KB" -f (Split-Path $dst -Leaf), ((Get-Item $dst).Length / 1KB))
}
Write-Host "done: $theme captures in $docs"
