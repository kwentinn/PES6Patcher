# Applies a named memory patch (research/patches.json) to the game you are about to play, so you can try it by feel.
#   play-with-patch.ps1                 waits for PES6.exe (start the game yourself), then applies pace12
#   play-with-patch.ps1 -Patch pace12 -Revert     puts the original bytes back (the game keeps running)
# The patch lives in the game's memory: it stays through every match until you close the game. Nothing is written to disk,
# a restart of the game starts without it. The patch is applied once the game's code is unpacked (its first bytes read as the
# original ones) and a few seconds after, while you are still in the menus.
param([string]$Patch = 'pace12', [switch]$Revert, [int]$SettleSeconds = 10)

$pes6r = Join-Path $PSScriptRoot '..\Pes6Research\bin\pes6r.exe'
if (-not (Test-Path $pes6r)) { throw "$pes6r not found: build research/Pes6Research first (dotnet build -c Release)" }

if ($Revert) { & $pes6r patch -Revert $Patch; exit $LASTEXITCODE }

Write-Host "Waiting for the game (start PES6 yourself)..."
while (-not (Get-Process PES6 -ErrorAction SilentlyContinue)) { Start-Sleep 2 }
Write-Host "Game found. Waiting for its code to be ready..."
for ($i = 0; $i -lt 90; $i++) {
    $state = (& $pes6r patch -List 2>&1 | Where-Object { $_ -match "^$Patch\s" }) -join ' '
    if ($state -match "^$Patch\s+original") { break }
    Start-Sleep 2
}
Start-Sleep $SettleSeconds
& $pes6r patch -Apply $Patch
Write-Host "Done: $Patch stays until you close the game. Revert: play-with-patch.ps1 -Patch $Patch -Revert"
