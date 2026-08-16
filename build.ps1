<#
  Hoodrich build script.

  Uses the self-contained Roslyn compiler in tools\ rather than `dotnet build`,
  because the machine's .NET 8 SDK is broken (Microsoft.NETCore.App\8.0.28 is a
  partial install and every `dotnet` invocation dies on a missing hostpolicy.dll).
  This path needs no SDK, no Visual Studio and no admin rights.

  Usage:
    .\build.ps1                 # build to .\build\Hoodrich.dll
    .\build.ps1 -Deploy         # build, then copy dll + data into the game's scripts\
    .\build.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$Deploy,

    # Which install(s) -Deploy writes to. Hoodrich is a pure SHVDN script with no asset
    # dependencies, and both editions ship the same ScriptHookVDotNet3.dll, so one build
    # runs on both.
    [ValidateSet('Legacy', 'Enhanced', 'Both')]
    [string]$Target = 'Both',

    [string]$GtaDir = 'C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V',
    [string]$EnhancedDir = 'C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$csc = Join-Path $root 'tools\roslyn\tasks\net472\csc.exe'
$refDir = Join-Path $root 'tools\refasm\build\.NETFramework\v4.8'
$srcDir = Join-Path $root 'src\Hoodrich'
$outDir = Join-Path $root 'build'
$outDll = Join-Path $outDir 'Hoodrich.dll'

if (-not (Test-Path $csc)) { throw "Compiler missing: $csc  (see tools\README.md)" }
if (-not (Test-Path $refDir)) { throw "net48 reference assemblies missing: $refDir" }

$shvdn = Join-Path $GtaDir 'ScriptHookVDotNet3.dll'
if (-not (Test-Path $shvdn)) { throw "ScriptHookVDotNet3.dll not found under: $GtaDir" }

New-Item -ItemType Directory -Force $outDir | Out-Null

# --- references -------------------------------------------------------------
# Deliberately minimal. Hoodrich has ZERO external runtime dependencies: only the
# BCL and SHVDN. No Newtonsoft, no LemonUI, no NativeUI -- nothing that can lose a
# version fight with another mod in scripts\.
$refNames = @(
    'mscorlib.dll'
    'System.dll'
    'System.Core.dll'
    'System.Drawing.dll'
    'System.Windows.Forms.dll'
    'System.Xml.dll'
    'System.Numerics.dll'
)
$refs = @()
foreach ($n in $refNames) {
    $p = Join-Path $refDir $n
    if (-not (Test-Path $p)) { throw "Reference assembly missing: $p" }
    $refs += "/reference:`"$p`""
}
$refs += "/reference:`"$shvdn`""

# --- sources ----------------------------------------------------------------
$sources = Get-ChildItem $srcDir -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    ForEach-Object { $_.FullName }

if (-not $sources) { throw "No .cs sources found under $srcDir" }

# --- compiler options -------------------------------------------------------
$opts = @(
    '/target:library'
    '/platform:x64'
    '/langversion:9.0'
    '/nologo'
    '/warnaserror-'
    '/warn:4'
    '/nostdlib+'
    '/utf8output'
    "/out:`"$outDll`""
)
if ($Configuration -eq 'Debug') {
    $opts += '/debug:portable', '/define:DEBUG;TRACE', '/optimize-'
} else {
    $opts += '/debug-', '/optimize+'
}

$rsp = Join-Path $outDir 'build.rsp'
($opts + $refs + ($sources | ForEach-Object { "`"$_`"" })) | Set-Content -Path $rsp -Encoding UTF8

Write-Host "Compiling $($sources.Count) source files -> $outDll ($Configuration)" -ForegroundColor Cyan
$sw = [Diagnostics.Stopwatch]::StartNew()
& $csc "@$rsp"
$exit = $LASTEXITCODE
$sw.Stop()

if ($exit -ne 0) { throw "Compilation failed (csc exit $exit)." }
Write-Host ("OK  {0:N0} bytes in {1:N1}s" -f (Get-Item $outDll).Length, $sw.Elapsed.TotalSeconds) -ForegroundColor Green

# --- deploy -----------------------------------------------------------------
function Deploy-To([string]$gameDir, [string]$label) {
    if (-not (Test-Path $gameDir)) {
        Write-Host "skip $label - not installed at $gameDir" -ForegroundColor DarkGray
        return
    }

    $scripts = Join-Path $gameDir 'scripts'
    if (-not (Test-Path $scripts)) {
        Write-Host "skip $label - no scripts folder (ScriptHookVDotNet not installed?)" -ForegroundColor Yellow
        return
    }

    Write-Host "$label -> $scripts" -ForegroundColor Cyan

    Copy-Item $outDll $scripts -Force
    if (Test-Path (Join-Path $outDir 'Hoodrich.pdb')) {
        Copy-Item (Join-Path $outDir 'Hoodrich.pdb') $scripts -Force
    }

    # Config + data: never clobber the player's edited copies.
    $dataSrc = Join-Path $root 'data'
    $dataDst = Join-Path $scripts 'Hoodrich'
    New-Item -ItemType Directory -Force $dataDst | Out-Null
    Get-ChildItem $dataSrc -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($dataSrc.Length).TrimStart('\')
        $dst = Join-Path $dataDst $rel
        New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
        if (Test-Path $dst) {
            Write-Host "  keep   $rel" -ForegroundColor DarkGray
        } else {
            Copy-Item $_.FullName $dst
            Write-Host "  new    $rel" -ForegroundColor DarkGray
        }
    }

    $iniSrc = Join-Path $root 'Hoodrich.ini'
    $iniDst = Join-Path $scripts 'Hoodrich.ini'
    if ((Test-Path $iniSrc) -and -not (Test-Path $iniDst)) { Copy-Item $iniSrc $iniDst }
}

if ($Deploy) {
    $running = Get-Process GTA5, GTA5_Enhanced -ErrorAction SilentlyContinue
    if ($running) { throw "GTA V is running - close it before deploying (the dll is locked)." }

    if ($Target -in 'Legacy', 'Both')   { Deploy-To $GtaDir      'Legacy' }
    if ($Target -in 'Enhanced', 'Both') { Deploy-To $EnhancedDir 'Enhanced' }

    Write-Host "Deploy complete." -ForegroundColor Green
}
