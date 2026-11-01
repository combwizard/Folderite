# Builds the demo X:\ drive (made-up folders), and a portable demo copy of Folderite with its own
# config.json and history.json, under .work\. Called by capture.ps1.
$ErrorActionPreference = 'Stop'
$work = Join-Path $PSScriptRoot '.work'
# On C: (no volume label) so Explorer shows the demo drive as "Local Disk (X:)", not a real drive's name.
$root = Join-Path ([IO.Path]::GetTempPath()) 'folderite-demo-root'
$app  = Join-Path $work 'app'
$project = Join-Path $PSScriptRoot '..\..\src\Folderite\Folderite.csproj'

if (Test-Path $root) { Remove-Item $root -Recurse -Force }
$tree = @{
  'Projects\aurora-web'        = 'README.md','package.json','tsconfig.json','vite.config.ts'
  'Projects\aurora-web\src'    = 'main.ts','App.vue','router.ts','store.ts'
  'Projects\aurora-web\public' = 'favicon.svg','robots.txt'
  'Projects\game-jam'          = 'README.md','project.godot','export_presets.cfg'
  'Projects\game-jam\assets'   = 'player.png','tileset.png','theme.ogg'
  'Projects\notes-cli'         = 'README.md','Cargo.toml','LICENSE'
  'Projects\notes-cli\docs'    = 'usage.md','config.md'
  'Projects\notes-cli\src'     = 'main.rs','store.rs'
  'Projects\website'           = 'index.html','style.css'
  'Documents'                  = 'Budget 2026.xlsx','Lease agreement.pdf','Passport scan.pdf'
  'Documents\Taxes 2026'       = 'Return.pdf','Receipts.zip'
  'Documents\Invoices'         = 'INV-1041.pdf','INV-1042.pdf','INV-1043.pdf'
  'Documents\Letters'          = 'Reference letter.docx'
  'Photos\2026\Iceland trip'   = 'IMG_2201.jpg','IMG_2202.jpg','IMG_2203.jpg','IMG_2207.jpg','IMG_2214.jpg'
  'Photos\2026\Summer'         = 'IMG_3010.jpg','IMG_3011.jpg'
  'Photos\Wallpapers'          = 'aurora.jpg','dunes.jpg','fjord.png'
  'Downloads'                  = 'setup-7zip.exe','report-final.pdf','font-pack.zip','screenshot.png'
  'Downloads\installers'       = 'blender-5.0-windows-x64.msi','vscode-setup.exe'
  'Music\Playlists'            = 'Focus.m3u','Running.m3u'
  'Music\Albums'               = @()
  'Work\Specs'                 = 'Search v2.docx','Sync protocol.md','Onboarding flow.pdf'
  'Work\Reports'               = 'Q3 review.pptx','Q3 numbers.xlsx','Weekly 44.docx'
  'Work\Design\Mockups'        = 'home.fig','settings.fig','export.png'
  'Work\Design'                = 'Brand guide.pdf'
}
foreach ($dir in $tree.Keys) {
  $d = Join-Path $root $dir
  New-Item -ItemType Directory -Force $d | Out-Null
  $i = 0
  foreach ($f in $tree[$dir]) {
    $p = Join-Path $d $f
    [IO.File]::WriteAllBytes($p, [byte[]]::new(1024 * (3 + 17 * $i++)))
    (Get-Item $p).LastWriteTime = (Get-Date '2026-10-30 14:00').AddHours(-37 * $i)
  }
}
foreach ($d in Get-ChildItem $root -Recurse -Directory) { $d.LastWriteTime = (Get-Date '2026-10-29 10:12').AddMinutes(-($d.FullName.Length * 53)) }

# Map X: to the fake root.
subst X: /D 2>$null | Out-Null
subst X: $root
if (-not (Test-Path 'X:\Projects')) { throw 'subst X: failed (is X: in use?)' }

# Portable demo app: config.json next to the exe always wins.
if (Test-Path $app) { Remove-Item $app -Recurse -Force }
dotnet publish $project -c Release -o $app -v q -nologo | Out-Null
if (-not (Test-Path "$app\Folderite.exe")) { throw 'publish failed' }

@'
{
  "favorites": [
    { "name": "&Projects",  "path": "X:\\Projects" },
    { "name": "&Documents", "path": "X:\\Documents" },
    { "name": "P&hotos",    "path": "X:\\Photos" },
    { "name": "Do&wnloads", "path": "X:\\Downloads" },
    { "name": "-" },
    { "name": "W&ork", "items": [
      { "name": "&Specs",   "path": "X:\\Work\\Specs" },
      { "name": "&Reports", "path": "X:\\Work\\Reports" },
      { "name": "&Design",  "path": "X:\\Work\\Design" }
    ]}
  ],
  "recentCount": 8,
  "historyExclude": [],
  "actions": [
    { "name": "Open &terminal here", "command": "wt.exe", "args": "-d \"{path}\"" },
    { "name": "Open in &VS Code",    "command": "code",   "args": "\"{path}\"" }
  ],
  "disabledProcesses": [],
  "showOpenWindows": true,
  "showClipboard": true,
  "enableOnDesktop": true
}
'@ | Set-Content "$app\config.json" -Encoding utf8

$now = [DateTimeOffset]::Now
$recent = @(
  @('X:\Projects\aurora-web\src', 42, 1),
  @('X:\Photos\2026\Iceland trip', 30, 5),
  @('X:\Documents\Taxes 2026', 24, 20),
  @('X:\Projects\game-jam\assets', 21, 26),
  @('X:\Work\Design\Mockups', 15, 50),
  @('X:\Downloads\installers', 11, 70),
  @('X:\Music\Playlists', 8, 120),
  @('X:\Documents\Invoices', 6, 200)
)
$entries = foreach ($r in $recent) { [ordered]@{ path = $r[0]; rank = $r[1]; lastAccess = $now.AddHours(-$r[2]).ToString('o') } }
[ordered]@{ version = 1; entries = @($entries) } | ConvertTo-Json -Depth 4 | Set-Content "$app\history.json" -Encoding utf8
"setup: X: -> $root; app -> $app"
