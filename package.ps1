<#
.SYNOPSIS
  Builds the FO4RecordEditor release zip — the thing you hand to other people.

.DESCRIPTION
  Produces a framework-dependent folder build (NOT self-contained: MO2's usvfs cannot load a
  self-contained .NET host's bundled runtime, so the app would die silently before managed code
  runs), bundles the external tools it shells out to, adds the docs and a sample MCP config, and
  zips the result.

  Recipients need the .NET 9 Desktop Runtime (x64) and nothing else.

.PARAMETER Version
  Version string in the zip name. Defaults to today's date.

.PARAMETER SkipWeb
  Skip the npm build of the web UI. Only safe if web\dist\ is already current.

.PARAMETER SkipCkWiki
  Skip bundling the offline Creation Kit Wiki mirror (~160MB). Bundled by default so
  papyrus_function_lookup / papyrus_script_info work out of the box for recipients with no setup.
  Use this for a fast local iteration build; never for a build you're actually handing to someone.

.PARAMETER SkipAudioTools
  Skip bundling ffmpeg/xWMAEncode/BmlFuzEncode/BmlFuzDecode (~110MB, mostly ffmpeg). Bundled by
  default so the audio_* tools work out of the box. Use this for a fast local iteration build; never
  for a build you're actually handing to someone.

.PARAMETER IncludeBaseScripts
  Bundle the vanilla Papyrus base scripts from ..\papyrus\Base.

  OFF by default, deliberately: those are Bethesda's script sources and are not ours to
  redistribute. They are also unnecessary — compile_papyrus needs the Creation Kit's compiler
  anyway, and anyone who has the CK already has the base scripts, which the tool auto-detects from
  their Fallout 4 install. Turn this on only for a private build for yourself.

.EXAMPLE
  .\package.ps1
  .\package.ps1 -Version 1.2.0 -SkipWeb
#>
[CmdletBinding()]
param(
    [string] $Version = (Get-Date -Format 'yyyy.MM.dd'),
    [switch] $SkipWeb,
    [switch] $SkipCkWiki,
    [switch] $SkipAudioTools,
    [switch] $IncludeBaseScripts
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root      = $PSScriptRoot
$Csproj    = Join-Path $Root 'FO4RecordEditor\FO4RecordEditor.csproj'
$WebDir    = Join-Path $Root 'web'
$PublishIn = Join-Path $Root 'FO4RecordEditor\bin\Release\net9.0-windows\win-x64\publish'
$Staging   = Join-Path $Root "dist\FO4RecordEditor-$Version"
$ZipPath   = Join-Path $Root "dist\FO4RecordEditor-$Version.zip"

$Niftool   = Join-Path $Root '..\tools\niftool\build\windows\x64\release\niftool.exe'
$Texconv   = Join-Path $Root 'TES5Edit-dev-4.1.6\Build\Edit Scripts\Texconvx64.exe'
$BaseScripts = Join-Path $Root '..\papyrus\Base'
$CkWiki    = 'E:\F4SE OG\docs\Knowledge Materials\Creation Kit Wiki\Fallout 4 Creation Kit Wiki-52183-21-05-20-1621512739\FO4CKWiki_210520\fallout4'
$AudioToolsDir = 'E:\F4SE OG\Tools\Audio Converter\bin'

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Warn($msg) { Write-Host "    ! $msg" -ForegroundColor Yellow }

# A running exe locks the build output, and a live MCP server is a running exe. This is the most
# common cause of a mysterious "build failed" here.
Step 'Stopping any running FO4RecordEditor.exe'
Get-Process FO4RecordEditor -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

if (-not $SkipWeb) {
    Step 'Building the web UI'
    Push-Location $WebDir
    try {
        if (Test-Path 'package-lock.json') { npm ci } else { npm install }
        if ($LASTEXITCODE -ne 0) { throw 'npm install failed.' }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'npm run build failed.' }
    } finally { Pop-Location }
} else {
    Warn 'Skipping the web build (-SkipWeb). web\dist\ had better be current.'
}

if (-not (Test-Path (Join-Path $WebDir 'dist'))) {
    throw "web\dist\ does not exist. Run without -SkipWeb — the exe will not render its UI without it."
}

Step 'Publishing (framework-dependent, x64)'
dotnet publish $Csproj -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=false -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed. Scroll up for the error.' }

Step 'Staging the package'
if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
New-Item -ItemType Directory -Path $Staging -Force | Out-Null

Copy-Item "$PublishIn\*" $Staging -Recurse -Force

# Never ship build debris or a developer's own settings/logs.
Get-ChildItem $Staging -Include '*.pdb', '*.startup.log', 'settings.json' -Recurse -File |
    Remove-Item -Force -ErrorAction SilentlyContinue

Step 'Bundling external tools'
if (Test-Path $Niftool) {
    $dest = Join-Path $Staging 'tools\niftool'
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item $Niftool $dest -Force
    Write-Host '    niftool.exe'
} else {
    Warn "niftool.exe not found at $Niftool — the nif_* tools will be dead in this package."
}

if (Test-Path $Texconv) {
    $dest = Join-Path $Staging 'tools\texconv'
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item $Texconv $dest -Force
    Write-Host '    Texconvx64.exe'
} else {
    Warn "Texconvx64.exe not found — texture previews will not render."
}

if (-not $SkipCkWiki) {
    if (Test-Path $CkWiki) {
        $dest = Join-Path $Staging 'tools\ckwiki\fallout4'
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
        Copy-Item "$CkWiki\*" $dest -Recurse -Force
        $count = (Get-ChildItem $dest -Filter '*.html' -Recurse -File).Count
        Write-Host "    CK Wiki mirror ($count pages)"
    } else {
        Warn "CK wiki mirror not found at $CkWiki — papyrus_function_lookup/papyrus_script_info will be dead in this package unless the recipient sets CkWikiPath themselves."
    }
} else {
    Warn 'Skipping the CK Wiki mirror (-SkipCkWiki). Fine for a local iteration build, not for one you hand to someone.'
}

if (-not $SkipAudioTools) {
    if (Test-Path $AudioToolsDir) {
        $dest = Join-Path $Staging 'tools\audio'
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
        $missing = @()
        foreach ($exe in 'ffmpeg.exe', 'xWMAEncode.exe', 'BmlFuzEncode.exe', 'BmlFuzDecode.exe') {
            $src = Join-Path $AudioToolsDir $exe
            if (Test-Path $src) { Copy-Item $src $dest -Force } else { $missing += $exe }
        }
        if ($missing.Count -eq 0) { Write-Host '    ffmpeg.exe, xWMAEncode.exe, BmlFuzEncode.exe, BmlFuzDecode.exe' }
        else { Warn "Missing from ${AudioToolsDir}: $($missing -join ', ') — the audio_* tools will be partially dead in this package." }
    } else {
        Warn "Audio tools folder not found at $AudioToolsDir — the audio_* tools will be dead in this package unless the recipient sets FfmpegPath/XwmaEncodePath themselves."
    }
} else {
    Warn 'Skipping the audio tools (-SkipAudioTools). Fine for a local iteration build, not for one you hand to someone.'
}

if ($IncludeBaseScripts) {
    if (Test-Path $BaseScripts) {
        $dest = Join-Path $Staging 'tools\papyrus\Base'
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
        Copy-Item "$BaseScripts\*" $dest -Recurse -Force
        Warn 'Bundled the vanilla Papyrus base scripts. These are Bethesda''s — do not redistribute this build.'
    } else {
        Warn "-IncludeBaseScripts was set but $BaseScripts does not exist."
    }
}

Step 'Adding docs and the sample MCP config'
foreach ($f in 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md') {
    Copy-Item (Join-Path $Root $f) $Staging -Force
}

# Allowlist, never `Copy-Item docs -Recurse`: this repo's docs/ also held internal planning notes and
# redirect stubs pointing at a private workspace path, and a recursive copy shipped them to end users.
# This matters more since 2026-08-07, not less: docs/internal/ now holds the whole engineering
# knowledge base, versioned with the code. This allowlist is the only thing keeping it out of the
# release zip.
$ShippedDocs = 'MCP_SETUP.md'
$DocsDest = New-Item -ItemType Directory -Path (Join-Path $Staging 'docs') -Force
foreach ($d in $ShippedDocs) {
    $src = Join-Path $Root "docs\$d"
    if (-not (Test-Path $src)) { throw "Shipped doc missing: $src" }
    Copy-Item $src $DocsDest -Force
}

# The path is left as a placeholder on purpose: it must be the recipient's own absolute path, and a
# stale one silently starts nothing.
@'
{
  "mcpServers": {
    "fo4editor": {
      "type": "stdio",
      "command": "C:\\PUT\\THE\\FULL\\PATH\\HERE\\FO4RecordEditor.exe",
      "args": ["--mcp", "--mo2", "C:\\PUT\\YOUR\\MO2\\INSTANCE\\HERE"]
    }
  }
}
'@ | Set-Content (Join-Path $Staging 'mcp.sample.json') -Encoding UTF8

Step 'Zipping'
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$Staging\*" -DestinationPath $ZipPath -CompressionLevel Optimal

$sizeMb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host "`n============================================================" -ForegroundColor Green
Write-Host " Packaged: $ZipPath" -ForegroundColor Green
Write-Host " Size:     $sizeMb MB"
Write-Host " Needs:    .NET 9 Desktop Runtime (x64) on the target machine"
Write-Host "============================================================`n" -ForegroundColor Green
