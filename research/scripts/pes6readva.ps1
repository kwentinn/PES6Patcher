# Read values of the exe file at virtual addresses: float, int32 and bytes.
# Usage: pes6readva.ps1 -Exe PES6.exe.orig -Addresses 0xB8C10C,0x5E23FC -Length 8   (works on the file: the game need not run)
param([string]$Exe, [uint32[]]$Addresses, [int]$Length = 8)
$b = [IO.File]::ReadAllBytes($Exe)
$pe = [BitConverter]::ToInt32($b, 0x3C); $n = [BitConverter]::ToUInt16($b, $pe + 6); $opt = [BitConverter]::ToUInt16($b, $pe + 20)
$base = [BitConverter]::ToUInt32($b, $pe + 24 + 28); $sec = $pe + 24 + $opt
foreach ($a in $Addresses) {
    $hit = $false
    for ($i = 0; $i -lt $n; $i++) { $o = $sec + 40 * $i
        $va = $base + [BitConverter]::ToUInt32($b, $o + 12); $rs = [BitConverter]::ToUInt32($b, $o + 16); $raw = [BitConverter]::ToUInt32($b, $o + 20)
        if ($a -ge $va -and $a -lt $va + $rs) { $f = $raw + $a - $va; $hit = $true
            "0x{0:X}: f={1} i={2} bytes={3}" -f $a, [BitConverter]::ToSingle($b, $f), [BitConverter]::ToInt32($b, $f), (($b[$f..($f + $Length - 1)] | ForEach-Object { $_.ToString('X2') }) -join ' ') } }
    if (-not $hit) { "0x{0:X}: not in the file" -f $a }
}
