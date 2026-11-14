# Applies or reverts named memory patches (research/patches.json) in the running game: one command to try an idea
# and one to put the game back, with the original bytes checked first so nothing is written over another patch.
# Usage: pes6patch.ps1 -List
#        pes6patch.ps1 -Apply defence-line
#        pes6patch.ps1 -Revert defence-line
#        pes6patch.ps1 -RevertAll
# A patch whose bytes are neither the original nor the patched ones (another build, another patch) is refused unless -Force.
# To add a patch, add an entry to patches.json with the bytes read by pes6code.ps1 BEFORE the change (original) and the new ones.
param([switch]$List, [string[]]$Apply = @(), [string[]]$Revert = @(), [switch]$RevertAll, [switch]$Force,
      [string]$File = (Join-Path $PSScriptRoot '..\patches.json'))

$patches = Get-Content $File -Raw | ConvertFrom-Json
$code = Join-Path $PSScriptRoot 'pes6code.ps1'
$running = [bool](Get-Process PES6 -ErrorAction SilentlyContinue)

function Norm([string]$hex) { ($hex -split '\s+' | Where-Object { $_ } | ForEach-Object { $_.ToUpper().PadLeft(2, '0') }) -join ' ' }
function Read-Bytes([int]$address, [int]$length) {
    $line = & $code -Address $address -Length $length
    Norm (($line -split ': ', 2)[1])
}
function State($entry) {
    $a = [Convert]::ToInt32($entry.address, 16); $n = (Norm $entry.original).Split(' ').Count
    $now = Read-Bytes $a $n
    if ($now -eq (Norm $entry.patched)) { 'applied' } elseif ($now -eq (Norm $entry.original)) { 'original' } else { "other ($now)" }
}
function Set-Patch([string]$name, [bool]$on) {
    $p = $patches.$name
    if (-not $p) { Write-Error "unknown patch '$name' (see -List)"; return }
    $states = $p.bytes | ForEach-Object { State $_ }
    $want = if ($on) { 'applied' } else { 'original' }
    if (-not $Force -and ($states | Where-Object { $_ -like 'other*' })) { Write-Error "$name : unexpected bytes ($($states -join ' ; ')): another build or patch? use -Force to write anyway"; return }
    foreach ($e in $p.bytes) {
        $hex = if ($on) { $e.patched } else { $e.original }
        & $code -Address ([Convert]::ToInt32($e.address, 16)) -Hex $hex | Out-Null
    }
    $after = $p.bytes | ForEach-Object { State $_ }
    "{0}: {1}" -f $name, $(if (($after | Where-Object { $_ -ne $want }).Count -eq 0) { $want } else { "NOT $want ($($after -join ' ; '))" })
}

if (-not $running -and ($Apply.Count -or $Revert.Count -or $RevertAll)) { Write-Error 'PES6 is not running.'; exit 1 }
if ($List -or -not ($Apply.Count -or $Revert.Count -or $RevertAll)) {
    foreach ($name in $patches.PSObject.Properties.Name) {
        $state = if ($running) { ($patches.$name.bytes | ForEach-Object { State $_ } | Select-Object -Unique) -join ' / ' } else { 'game not running' }
        "{0,-20} {1,-16} {2}" -f $name, $state, $patches.$name.about
    }
}
if ($RevertAll) { $Revert = $patches.PSObject.Properties.Name }
foreach ($n in $Apply) { Set-Patch $n $true }
foreach ($n in $Revert) { Set-Patch $n $false }
