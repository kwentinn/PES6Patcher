# Build byte signatures for code locations of the reference PES6.exe: the bytes around each
# location, with every absolute address (0x400000-0x5200000) and every rel32 call/jmp target
# masked as ??, grown until the pattern matches exactly once in .text.
# Usage: make-signatures.ps1 -Exe PES6.orig.exe -Sites 0x8744F0,0x40D740
param([string]$Exe, [int[]]$Sites, [int]$Before = 8, [int]$Max = 64)

$b = [IO.File]::ReadAllBytes($Exe)
$textStart = 0x400; $textEnd = 0x77C400            # raw range of .text
function Off([int]$va) { $va - 0x400C00 }

# mask[i] = $true when byte i of the file is part of a relocatable value
$mask = New-Object bool[] $b.Length
for ($i = $textStart; $i -lt $textEnd - 4; $i++) {
    $v = [BitConverter]::ToInt32($b, $i)
    if ($v -ge 0x400000 -and $v -lt 0x5200000) { for ($k = 0; $k -lt 4; $k++) { $mask[$i + $k] = $true } }
    if (($b[$i] -eq 0xE8 -or $b[$i] -eq 0xE9) -and $i + 5 -le $textEnd) {
        $target = ($i + 0x400C00) + 5 + [BitConverter]::ToInt32($b, $i + 1)
        if ($target -ge 0x401000 -and $target -lt 0x5200000) { for ($k = 1; $k -le 4; $k++) { $mask[$i + $k] = $true } }
    }
}

function Count([int]$start, [int]$len) {
    $n = 0
    $first = $b[$start]; $firstMasked = $mask[$start]
    for ($p = $textStart; $p -le $textEnd - $len; $p++) {
        if (-not $firstMasked -and $b[$p] -ne $first) { continue }
        $ok = $true
        for ($k = 0; $k -lt $len; $k++) { if (-not $mask[$start + $k] -and $b[$p + $k] -ne $b[$start + $k]) { $ok = $false; break } }
        if ($ok) { $n++; if ($n -gt 1) { return $n } }
    }
    $n
}

foreach ($va in $Sites) {
    $o = Off $va
    $found = $false
    for ($len = 12; $len -le $Max; $len += 4) {
        $start = $o - [Math]::Min($Before, $len / 2)
        if ((Count $start $len) -eq 1) {
            $hex = for ($k = 0; $k -lt $len; $k++) { if ($mask[$start + $k]) { '??' } else { $b[$start + $k].ToString('X2') } }
            "0x{0:X}`toffset {1}`t{2}" -f $va, ($o - $start), ($hex -join ' ')
            $found = $true; break
        }
    }
    if (-not $found) { "0x{0:X}`tPAS UNIQUE en {1} octets" -f $va, $Max }
}
