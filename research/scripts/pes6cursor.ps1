# Memory-only test of the player-cursor fix on a running PES6 (v4 exe installed):
# the screen matrix built at 0x40D0AE scales x by 0.5 * W (W = 512). The 2D layer now uses
# a half-width of Half, so x must be scaled by 0.5 * (2 * Half) / 512 instead.
# Usage: pes6cursor.ps1 -Half 398      /     pes6cursor.ps1 -Revert
param([int]$Half = 398, [switch]$Revert)

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Ins = 0x40D0AE; $Orig = 0xB7D828; $New = 0xBC3F08   # .rdata padding next to the 2D constants

if ($Revert) {
    & "$dir\pes6retarget.ps1" -From $New -To $Orig -Instructions $Ins
} else {
    $v = [single](0.5 * (2 * $Half) / 512)
    & "$dir\pes6code.ps1" -Address $New -Length 4 -Hex (([BitConverter]::GetBytes($v) | ForEach-Object { $_.ToString('X2') }) -join ' ') | Out-Null
    "constante 0x{0:X} = {1}" -f $New, $v
    & "$dir\pes6retarget.ps1" -From $Orig -To $New -Instructions $Ins
}
