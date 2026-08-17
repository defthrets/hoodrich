# Draws every GTA V zone as an SVG you can paint gang colours onto.
#
# Turf is assigned per zone, so the question "which blocks does this crew hold" is really
# "which zones". This renders all of them from the game's own bounds -- real shapes, real
# positions, each one labelled and tagged with its zone code -- with the current turf already
# filled in.
#
# To reassign turf: open the SVG, fill the zones you want in a crew's colour, save, and run
# read_map.ps1 to turn it back into gangs.json turf lists.
#
# World coordinates run X -4000..4600 and Y -4200..8100. SVG y grows downward, so it is
# flipped here -- north is up on the output, the way the in-game map reads.

param(
    [string] $DumpPath   = (Join-Path $PSScriptRoot 'zones_dump.json'),
    [string] $GangsPath  = (Join-Path $PSScriptRoot '..\data\gangs.json'),
    [string] $OutputPath = (Join-Path $PSScriptRoot 'hoodrich_zones.svg')
)

$ErrorActionPreference = 'Stop'

$zones = Get-Content $DumpPath -Raw | ConvertFrom-Json
$gangData = Get-Content $GangsPath -Raw | ConvertFrom-Json

# Who currently holds what, and in which colour.
$owner = @{}
$colour = @{}
foreach ($g in $gangData.gangs) {
    $rgb = '#{0:x2}{1:x2}{2:x2}' -f [int]$g.colour[0], [int]$g.colour[1], [int]$g.colour[2]
    $colour[$g.id] = $rgb
    foreach ($code in $g.turf) { $owner[$code.ToUpper()] = $g.id }
}

$minX = -4000; $maxX = 4600
$minY = -4200; $maxY = 8100
$width = $maxX - $minX
$height = $maxY - $minY

$svg = [System.Text.StringBuilder]::new()
[void]$svg.AppendLine('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ' + $width + ' ' + $height + '" width="1290" height="1845">')
[void]$svg.AppendLine('  <rect x="0" y="0" width="' + $width + '" height="' + $height + '" fill="#12141a"/>')

# A 500 m grid, so a zone can be read off against world coordinates.
[void]$svg.AppendLine('  <g stroke="#2a2f3a" stroke-width="2" fill="none">')
for ($x = $minX; $x -le $maxX; $x += 500) {
    $sx = $x - $minX
    [void]$svg.AppendLine('    <line x1="' + $sx + '" y1="0" x2="' + $sx + '" y2="' + $height + '"/>')
}
for ($y = $minY; $y -le $maxY; $y += 500) {
    $sy = $maxY - $y
    [void]$svg.AppendLine('    <line x1="0" y1="' + $sy + '" x2="' + $width + '" y2="' + $sy + '"/>')
}
[void]$svg.AppendLine('  </g>')

# One group per zone, tagged with its code, so a fill can be read straight back out.
foreach ($z in ($zones | Sort-Object Name)) {
    $code = $z.Name.ToUpper()
    $held = $owner[$code]
    $fill = if ($held) { $colour[$held] } else { '#3a4050' }
    $op   = if ($held) { '0.75' } else { '0.35' }

    [void]$svg.AppendLine('  <g id="' + $code + '" data-zone="' + $code + '" data-gang="' + $held + '">')

    $cx = 0.0; $cy = 0.0; $n = 0
    foreach ($b in $z.Bounds) {
        $w = $b.Maximum.X - $b.Minimum.X
        $h = $b.Maximum.Y - $b.Minimum.Y
        if ($w -lt 5 -or $h -lt 5) { continue }

        $sx = $b.Minimum.X - $minX
        $sy = $maxY - $b.Maximum.Y

        [void]$svg.AppendLine('    <rect x="' + [math]::Round($sx,1) + '" y="' + [math]::Round($sy,1) +
                              '" width="' + [math]::Round($w,1) + '" height="' + [math]::Round($h,1) +
                              '" fill="' + $fill + '" fill-opacity="' + $op +
                              '" stroke="#0b0d12" stroke-width="3"/>')

        $cx += ($b.Minimum.X + $b.Maximum.X) / 2
        $cy += ($b.Minimum.Y + $b.Maximum.Y) / 2
        $n++
    }

    if ($n -gt 0) {
        $lx = [math]::Round(($cx / $n) - $minX, 1)
        $ly = [math]::Round($maxY - ($cy / $n), 1)
        $label = if ($z.DisplayName) { $z.DisplayName } else { $code }

        [void]$svg.AppendLine('    <text x="' + $lx + '" y="' + $ly +
                              '" fill="#e8ecf2" font-family="sans-serif" font-size="46" text-anchor="middle">' +
                              [System.Security.SecurityElement]::Escape($label) + '</text>')
        [void]$svg.AppendLine('    <text x="' + $lx + '" y="' + ($ly + 46) +
                              '" fill="#8e97a8" font-family="monospace" font-size="34" text-anchor="middle">' +
                              $code + '</text>')
    }

    [void]$svg.AppendLine('  </g>')
}

# Legend, so the colours mean something without opening gangs.json.
$ly = 120
[void]$svg.AppendLine('  <g font-family="sans-serif" font-size="64">')
foreach ($g in $gangData.gangs) {
    [void]$svg.AppendLine('    <rect x="120" y="' + ($ly - 50) + '" width="70" height="70" fill="' + $colour[$g.id] + '"/>')
    [void]$svg.AppendLine('    <text x="220" y="' + $ly + '" fill="#e8ecf2">' +
                          [System.Security.SecurityElement]::Escape($g.name) + '  (' + $g.id + ')</text>')
    $ly += 100
}
[void]$svg.AppendLine('  </g>')
[void]$svg.AppendLine('</svg>')

Set-Content $OutputPath $svg.ToString() -Encoding UTF8
Write-Host ("Wrote {0} zones to {1}" -f $zones.Count, $OutputPath)
