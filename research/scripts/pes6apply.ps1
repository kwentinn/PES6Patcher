# Apply the whole widescreen 2D fix to a running PES6 process (memory only).
# Half = virtual half-width: 398 for 16:9, 796 for 32:9.
param([int]$Half = 398)

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
& "$dir\pes6redirect.ps1" -Half $Half
& "$dir\pes6rect.ps1" -Width (2 * $Half) | Out-Null
& "$dir\pes6tables.ps1" -Width (2 * $Half)
