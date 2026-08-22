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

    # Overwrite the installed data files with the ones just built. Off by default so a
    # player's hand-edits survive, but generated content (turf, dealers, gangs) has to be
    # able to move or the game runs data that does not match the build.
    [switch]$FreshData,

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
function Read-IniKeys {
    <#
        Every "Section.Key" in an ini, so two of them can be compared by what they actually
        SET rather than by which headings they happen to have. Comments and blank lines are
        skipped; a key outside any section is ignored, because the parser in the mod ignores
        it too.
    #>
    param([string]$Path)

    $section = ''
    $keys = New-Object System.Collections.Generic.List[string]

    foreach ($line in (Get-Content -LiteralPath $Path)) {
        $t = $line.Trim()

        if ($t -match '^\[(.+)\]$') { $section = $Matches[1]; continue }
        if ($t.StartsWith(';') -or -not $t.Contains('=')) { continue }
        if (-not $section) { continue }

        $keys.Add("$section.$($t.Split('=')[0].Trim())")
    }

    return $keys
}

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

    # Config + data: never clobber the player's edited copies, UNLESS asked to.
    #
    # Most of these files are content rather than settings, and keeping a stale copy is not
    # being careful with somebody's edits, it is shipping a build that quietly does not match
    # its own data. -FreshData overwrites them; without it the keeps are listed loudly enough
    # to notice.
    $dataSrc = Join-Path $root 'data'
    $dataDst = Join-Path $scripts 'Hoodrich'
    New-Item -ItemType Directory -Force $dataDst | Out-Null

    $stale = @()

    Get-ChildItem $dataSrc -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($dataSrc.Length).TrimStart('\')
        $dst = Join-Path $dataDst $rel
        New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null

        if (-not (Test-Path $dst)) {
            Copy-Item $_.FullName $dst
            Write-Host "  new    $rel" -ForegroundColor DarkGray
            return
        }

        $same = (Get-FileHash $_.FullName).Hash -eq (Get-FileHash $dst).Hash
        if ($same) {
            Write-Host "  same   $rel" -ForegroundColor DarkGray
        } elseif ($FreshData) {
            Copy-Item $_.FullName $dst -Force
            Write-Host "  update $rel" -ForegroundColor Green
        } else {
            $stale += $rel
            Write-Host "  KEEP   $rel  (differs from source)" -ForegroundColor Yellow
        }
    }

    if ($stale -and -not $FreshData) {
        Write-Host "         The game is running data that is NOT what you just built." -ForegroundColor Yellow
        Write-Host "         Re-run with -FreshData to overwrite: $($stale -join ', ')" -ForegroundColor DarkGray
    }

    # Anything the mod no longer ships has to go, or it keeps being loaded.
    Get-ChildItem $dataDst -File -Filter *.json | ForEach-Object {
        if ($_.Name -eq 'save.json' -or $_.Name -eq 'save.json.bak') { return }
        if (Test-Path (Join-Path $dataSrc $_.Name)) { return }

        Remove-Item $_.FullName -Force
        Write-Host "  remove $($_.Name)  (no longer shipped)" -ForegroundColor DarkYellow
    }

    # The ini is never overwritten, because it is the one file players hand-edit. That
    # silently leaves new settings undocumented on disk after an update, so say so.
    $iniSrc = Join-Path $root 'Hoodrich.ini'
    $iniDst = Join-Path $scripts 'Hoodrich.ini'

    if (Test-Path $iniSrc) {
        if (-not (Test-Path $iniDst)) {
            Copy-Item $iniSrc $iniDst
            Write-Host "  new    Hoodrich.ini" -ForegroundColor DarkGray
        } else {
            # Compared KEY by key, not section by section.
            #
            # This used to compare section headings only, and only in one direction. So an ini
            # that still had every section the template has looked fine while carrying twenty
            # dead keys inside them -- which is exactly what happened: [TurfWars] was deleted
            # from the template months ago and sat in both deployed inis regardless, along with
            # four hideout prices, two map settings and a wheel texture. None of it was read by
            # anything, and the deploy said "keep" every single time.
            $srcKeys = Read-IniKeys $iniSrc
            $dstKeys = Read-IniKeys $iniDst

            $absent = $srcKeys | Where-Object { $dstKeys -notcontains $_ }
            $stale  = $dstKeys | Where-Object { $srcKeys -notcontains $_ }

            if ($absent) {
                Write-Host "  STALE  Hoodrich.ini is missing $($absent.Count) setting(s):" -ForegroundColor Yellow
                Write-Host "         $($absent -join ', ')" -ForegroundColor DarkGray
                Write-Host "         Defaults apply until they are added." -ForegroundColor DarkGray
            }

            if ($stale) {
                Write-Host "  STALE  Hoodrich.ini has $($stale.Count) setting(s) nothing reads:" -ForegroundColor Yellow
                Write-Host "         $($stale -join ', ')" -ForegroundColor DarkGray
                Write-Host "         Left alone -- it is your file. Delete them, or copy Hoodrich.ini over it." -ForegroundColor DarkGray
            }

            if (-not $absent -and -not $stale) {
                # Counted, not typed. The number was hardcoded at 70 and stayed at 70 through
                # every setting added since -- on a line whose entire job is telling you whether
                # your ini is current.
                Write-Host "  keep   Hoodrich.ini ($($srcKeys.Count) settings, all current)" -ForegroundColor DarkGray
            }
        }
    }
}

if ($Deploy) {
    $running = Get-Process GTA5, GTA5_Enhanced -ErrorAction SilentlyContinue
    if ($running) { throw "GTA V is running - close it before deploying (the dll is locked)." }

    if ($Target -in 'Legacy', 'Both')   { Deploy-To $GtaDir      'Legacy' }
    if ($Target -in 'Enhanced', 'Both') { Deploy-To $EnhancedDir 'Enhanced' }

    Write-Host "Deploy complete." -ForegroundColor Green
}
