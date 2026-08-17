# Regenerates data\turf.json from the game's own zone bounds.
#
# GTA V defines each zone as a union of axis-aligned boxes. That union is the real shape of
# the neighbourhood -- already cut along the freeways and avenues -- so gang turf is shaded
# as those boxes rather than as a rectangle or circle somebody placed by eye.
#
# Which gang holds which zone comes from data\gangs.json, so moving turf between crews is an
# edit there followed by a run of this script.
#
# The dump is DurtyFree/gta-v-data-dumps zones.json, kept alongside this script so the build
# does not need the network.

# Note the Path suffixes: PowerShell variable names are case insensitive, so a $Gangs
# parameter and a $gangs local are the same variable, and the loop silently reads a string.
param(
    [string] $DumpPath   = (Join-Path $PSScriptRoot 'zones_dump.json'),
    [string] $GangsPath  = (Join-Path $PSScriptRoot '..\data\gangs.json'),
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\data\turf.json')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $DumpPath)) { throw "Zone dump not found at $DumpPath" }

$zones = Get-Content $DumpPath -Raw | ConvertFrom-Json
$byName = @{}
foreach ($z in $zones) { $byName[$z.Name.ToUpper()] = $z }

$gangs = Get-Content $GangsPath -Raw | ConvertFrom-Json

$rows = [System.Collections.Generic.List[string]]::new()

foreach ($gang in $gangs.gangs) {
    foreach ($code in $gang.turf) {
        $zone = $byName[$code.ToUpper()]
        if (-not $zone) {
            Write-Warning "$($gang.id): no zone bounds for '$code' -- it will not be shaded."
            continue
        }

        foreach ($b in $zone.Bounds) {
            $x = [math]::Round(($b.Minimum.X + $b.Maximum.X) / 2, 1)
            $y = [math]::Round(($b.Minimum.Y + $b.Maximum.Y) / 2, 1)
            $w = [math]::Round($b.Maximum.X - $b.Minimum.X, 1)
            $h = [math]::Round($b.Maximum.Y - $b.Minimum.Y, 1)

            # Slivers are map noise, not turf.
            if ($w -lt 5 -or $h -lt 5) { continue }

            $rows.Add('    { "gang": "' + $gang.id + '", "zone": "' + $code.ToUpper() +
                      '", "x": ' + $x + ', "y": ' + $y + ', "w": ' + $w + ', "h": ' + $h + ' }')
        }
    }
}

$header = @'
{
  "_comment": [
    "Hoodrich gang turf, shaded on the map.",
    "",
    "Generated from the game's own zone bounds by tools\\make_turf.ps1 -- not drawn by hand.",
    "Every zone in GTA V is a union of boxes, and that union IS the shape of the",
    "neighbourhood, already following the freeways and avenues. So the shading lands where",
    "the map's own edges are.",
    "",
    "gang    id from gangs.json",
    "zone    GET_NAME_OF_ZONE code; ties the shading to turf ownership and capture",
    "x, y    centre of one box, world coordinates",
    "w, h    its size in metres, axis aligned",
    "",
    "To move turf between gangs, edit the turf lists in gangs.json and run make_turf.ps1."
  ],
  "areas": [
'@

$footer = @'

  ]
}
'@

Set-Content $OutputPath ($header + ($rows -join ",`r`n") + $footer) -Encoding UTF8

Write-Host ("Wrote {0} boxes to {1}" -f $rows.Count, $OutputPath)
