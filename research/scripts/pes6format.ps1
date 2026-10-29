# Switch PES6 between the 16:9 and 32:9 profiles: patched PES6.exe, kload.cfg (resolution,
# borderless window), lodmixer.dat (3D aspect) and the game's settings.dat (display
# resolution). PES6.exe needs administrator rights (UAC prompt).
# Usage: pes6format.ps1 -Format 32x9      /      pes6format.ps1 -Format 16x9
param([Parameter(Mandatory)][ValidateSet('16x9', '32x9')][string]$Format)

$ErrorActionPreference = 'Stop'
$game    = 'C:\Program Files (x86)\KONAMI\Pro Evolution Soccer 6'
$saves   = Join-Path $env:USERPROFILE 'Documents\KONAMI\Pro Evolution Soccer 6'
$profile = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "work\profiles\$Format"

if (Get-Process PES6, settings -ErrorAction SilentlyContinue) { Write-Error "Fermer PES6 et settings.exe avant de changer de format."; exit 1 }
foreach ($f in 'PES6.exe', 'kload.cfg', 'lodmixer.dat', 'settings.dat') {
    if (-not (Test-Path -LiteralPath "$profile\$f")) { Write-Error "Profil incomplet : $profile\$f manquant."; exit 1 }
}

# kitserver files are writable without elevation
Copy-Item -LiteralPath "$profile\kload.cfg"    -Destination "$game\kitserver\kload.cfg" -Force
Copy-Item -LiteralPath "$profile\lodmixer.dat" -Destination "$game\kitserver\lodmixer.dat" -Force
# display resolution of the game (bytes 4-7, checksum = 16-bit sum of bytes 4..end at offset 2)
Copy-Item -LiteralPath "$profile\settings.dat" -Destination "$saves\settings.dat" -Force

# the game folder itself needs administrator rights
$src = "$profile\PES6.exe"
$cmd = "`$ErrorActionPreference='Stop'; Copy-Item -LiteralPath '$src' -Destination '$game\PES6.exe' -Force"
$enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cmd))
$proc = Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -EncodedCommand $enc" -Wait -PassThru

$ok = $true
foreach ($pair in @(@("$profile\PES6.exe", "$game\PES6.exe"), @("$profile\kload.cfg", "$game\kitserver\kload.cfg"),
                    @("$profile\lodmixer.dat", "$game\kitserver\lodmixer.dat"), @("$profile\settings.dat", "$saves\settings.dat"))) {
    if ((Get-FileHash -LiteralPath $pair[0]).Hash -ne (Get-FileHash -LiteralPath $pair[1]).Hash) { Write-Warning "Non appliqué : $($pair[1])"; $ok = $false }
}
if ($ok) { "Format $Format installé." } else { exit 1 }
