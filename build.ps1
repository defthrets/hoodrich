<#
  Trapline build script.

  Uses the self-contained Roslyn compiler in tools\ rather than `dotnet build`,
  because the machine's .NET 8 SDK is broken (Microsoft.NETCore.App\8.0.28 is a
  partial install and every `dotnet` invocation dies on a missing hostpolicy.dll).
  This path needs no SDK, no Visual Studio and no admin rights.

  Usage:
    .\build.ps1                 # build to .\build\Trapline.dll
    .\build.ps1 -Deploy         # build, then copy dll + data into the game's scripts\
    .\build.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$Deploy,
    [string]$GtaDir = 'C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$csc = Join-Path $root 'tools\roslyn\tasks\net472\csc.exe'
$refDir = Join-Path $root 'tools\refasm\build\.NETFramework\v4.8'
$srcDir = Join-Path $root 'src\Trapline'
$outDir = Join-Path $root 'build'
$outDll = Join-Path $outDir 'Trapline.dll'

if (-not (Test-Path $csc)) { throw "Compiler missing: $csc  (see tools\README.md)" }
if (-not (Test-Path $refDir)) { throw "net48 reference assemblies missing: $refDir" }

$shvdn = Join-Path $GtaDir 'ScriptHookVDotNet3.dll'
if (-not (Test-Path $shvdn)) { throw "ScriptHookVDotNet3.dll not found under: $GtaDir" }

New-Item -ItemType Directory -Force $outDir | Out-Null

# --- references -------------------------------------------------------------
# Deliberately minimal. Trapline has ZERO external runtime dependencies: only the
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
if ($Deploy) {
    $scripts = Join-Path $GtaDir 'scripts'
    if (-not (Test-Path $scripts)) { throw "scripts folder not found: $scripts" }

    $running = Get-Process GTA5, GTA5_Enhanced -ErrorAction SilentlyContinue
    if ($running) { throw "GTA V is running - close it before deploying (the dll is locked)." }

    Copy-Item $outDll $scripts -Force
    if (Test-Path (Join-Path $outDir 'Trapline.pdb')) {
        Copy-Item (Join-Path $outDir 'Trapline.pdb') $scripts -Force
    }

    # Config + data: never clobber the player's edited copies.
    $dataSrc = Join-Path $root 'data'
    $dataDst = Join-Path $scripts 'Trapline'
    New-Item -ItemType Directory -Force $dataDst | Out-Null
    Get-ChildItem $dataSrc -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($dataSrc.Length).TrimStart('\')
        $dst = Join-Path $dataDst $rel
        New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
        if (Test-Path $dst) {
            Write-Host "  keep   $rel (already present)" -ForegroundColor DarkGray
        } else {
            Copy-Item $_.FullName $dst
            Write-Host "  new    $rel" -ForegroundColor DarkGray
        }
    }

    $iniSrc = Join-Path $root 'Trapline.ini'
    $iniDst = Join-Path $scripts 'Trapline.ini'
    if ((Test-Path $iniSrc) -and -not (Test-Path $iniDst)) { Copy-Item $iniSrc $iniDst }

    Write-Host "Deployed to $scripts" -ForegroundColor Green
}
