# Applies a memory patch to the game you are about to play, so you can try it by feel.
#   play-with-patch.ps1                      waits for PES6.exe (start the game yourself), then applies the patcher's off-ball pace at 100 %
#   play-with-patch.ps1 -Percent 150         the same at another value (25 to 400)
#   play-with-patch.ps1 -Patch pace12        a named patch of research/patches.json instead (hand assembled tests)
#   play-with-patch.ps1 -Revert              puts the original bytes back (the game keeps running); with -Patch, that patch's
# The patch lives in the game's memory: it stays through every match until you close the game. Nothing is written to disk,
# a restart of the game starts without it. It is applied once the game's code is unpacked (its first bytes read as the
# original ones) and a few seconds after, while you are still in the menus. The off-ball pace is the patcher's own code
# (pes6r pace): open play only, corners, free kicks and the other restarts are the game's.
param([int]$Percent = 100, [string]$Patch = '', [switch]$Revert, [int]$SettleSeconds = 10)

$pes6r = Join-Path $PSScriptRoot '..\Pes6Research\bin\pes6r.exe'
if (-not (Test-Path $pes6r)) { throw "$pes6r not found: build research/Pes6Research first (dotnet build -c Release)" }

if ($Revert) { if ($Patch) { & $pes6r patch -Revert $Patch } else { & $pes6r pace -Revert }; exit $LASTEXITCODE }

Write-Host "Waiting for the game (start PES6 yourself)..."
while (-not (Get-Process PES6 -ErrorAction SilentlyContinue)) { Start-Sleep 2 }
Write-Host "Game found. Waiting for its code to be ready..."
# the entry of the first hook (0x4D34F5) reads as the game's own bytes once the code is unpacked
for ($i = 0; $i -lt 90; $i++) {
    $code = (& $pes6r code -Address 0x4D34F5 -Length 5 2>&1) -join ' '
    if ($code -match '8B 4E 04 33 D2') { break }
    Start-Sleep 2
}
Start-Sleep $SettleSeconds
if ($Patch) { & $pes6r patch -Apply $Patch } else { & $pes6r pace -Percent $Percent }
Write-Host "Done: it stays until you close the game. Revert: play-with-patch.ps1 -Revert"
