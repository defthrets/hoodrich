# Turns an edited hoodrich_zones.svg back into gang turf lists.
#
# Every zone in the SVG is a <g> tagged with its zone code. Fill any rectangle inside a zone
# with a crew's colour and that zone becomes theirs; leave it the neutral grey and it stays
# nobody's. Colours are matched to the nearest crew colour from gangs.json, so an approximate
# shade picked by eye in an editor still lands on the right crew.
#
# Writes the turf lists straight into gangs.json, then regenerate the map overlay:
#     .\tools\read_map.ps1
#     .\tools\make_turf.ps1

param(
    [string] $SvgPath   = (Join-Path $PSScriptRoot 'hoodrich_zones.svg'),
    [string] $GangsPath = (Join-Path $PSScriptRoot '..\data\gangs.json'),
    [switch] $WhatIf
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $SvgPath)) { throw "No map at $SvgPath" }

$gangData = Get-Content $GangsPath -Raw | ConvertFrom-Json

$palette = @{}
foreach ($g in $gangData.gangs) {
    $palette[$g.id] = @([int]$g.colour[0], [int]$g.colour[1], [int]$g.colour[2])
}

function ConvertFrom-HexColour([string] $hex) {
    $hex = $hex.Trim().TrimStart('#')
    if ($hex.Length -ne 6) { return $null }
    return @(
        [Convert]::ToInt32($hex.Substring(0, 2), 16),
        [Convert]::ToInt32($hex.Substring(2, 2), 16),
        [Convert]::ToInt32($hex.Substring(4, 2), 16)
    )
}

# Nearest crew colour, or nothing if it is closer to the neutral grey than to any crew.
function Resolve-Gang($rgb) {
    if (-not $rgb) { return $null }

    $neutral = @(0x3a, 0x40, 0x50)
    $bestId = $null
    $bestDistance = [double]::MaxValue

    foreach ($id in $palette.Keys) {
        $c = $palette[$id]
        $d = [math]::Pow($c[0] - $rgb[0], 2) + [math]::Pow($c[1] - $rgb[1], 2) + [math]::Pow($c[2] - $rgb[2], 2)
        if ($d -lt $bestDistance) { $bestDistance = $d; $bestId = $id }
    }

    $dn = [math]::Pow($neutral[0] - $rgb[0], 2) + [math]::Pow($neutral[1] - $rgb[1], 2) + [math]::Pow($neutral[2] - $rgb[2], 2)
    if ($dn -le $bestDistance) { return $null }

    return $bestId
}

$xml = [xml](Get-Content $SvgPath -Raw)
$turf = @{}
foreach ($id in $palette.Keys) { $turf[$id] = [System.Collections.Generic.List[string]]::new() }

$unassigned = 0

foreach ($g in $xml.svg.g) {
    $code = $g.GetAttribute('data-zone')
    if ([string]::IsNullOrEmpty($code)) { continue }

    # A zone is several boxes; the first painted one decides, so partial painting still works.
    $gang = $null
    foreach ($rect in @($g.rect)) {
        $gang = Resolve-Gang (ConvertFrom-HexColour $rect.fill)
        if ($gang) { break }
    }

    if ($gang) { $turf[$gang].Add($code) } else { $unassigned++ }
}

foreach ($g in $gangData.gangs) {
    $codes = $turf[$g.id]
    Write-Host ("{0,-12} {1,2} zones  {2}" -f $g.id, $codes.Count, ($codes -join ', '))
    $g.turf = @($codes)
}

if ($WhatIf) {
    Write-Host "`n$unassigned zones left neutral. Nothing written (-WhatIf)."
    return
}

$json = $gangData | ConvertTo-Json -Depth 12
Set-Content $GangsPath $json -Encoding UTF8

Write-Host ("`n{0} zones left neutral. Wrote {1}." -f $unassigned, $GangsPath)
Write-Host "Now run .\tools\make_turf.ps1 to rebuild the map overlay."
