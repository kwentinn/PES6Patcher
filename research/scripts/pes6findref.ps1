# Find the places of the exe file that hold a 32-bit address: prints the virtual address of each hit with
# the 8 bytes before and the 6 after.
# Usage: pes6findref.ps1 -Exe PES6.exe.orig -Values 0x3BCDCAE,0x3BE0A6E   (works on the file: the game need not run)
param([string]$Exe, [uint32[]]$Values)
$b = [IO.File]::ReadAllBytes($Exe)
$pe = [BitConverter]::ToInt32($b, 0x3C); $n = [BitConverter]::ToUInt16($b, $pe + 6); $opt = [BitConverter]::ToUInt16($b, $pe + 20)
$base = [BitConverter]::ToUInt32($b, $pe + 24 + 28); $sec = $pe + 24 + $opt
$secs = for ($i = 0; $i -lt $n; $i++) { $o = $sec + 40 * $i
    [pscustomobject]@{ name = [Text.Encoding]::ASCII.GetString($b, $o, 8).Trim([char]0); va = [BitConverter]::ToUInt32($b, $o + 12); rawSize = [BitConverter]::ToUInt32($b, $o + 16); raw = [BitConverter]::ToUInt32($b, $o + 20) } }
foreach ($v in $Values) {
    $pat = [BitConverter]::GetBytes($v)
    "== 0x{0:X}" -f $v
    for ($i = 8; $i -lt $b.Length - 10; $i++) {
        if ($b[$i] -eq $pat[0] -and $b[$i + 1] -eq $pat[1] -and $b[$i + 2] -eq $pat[2] -and $b[$i + 3] -eq $pat[3]) {
            $s = $secs | Where-Object { $i -ge $_.raw -and $i -lt $_.raw + $_.rawSize } | Select-Object -First 1
            if (-not $s) { continue }
            "{0,-6} 0x{1:X}: {2} [{3}] {4}" -f $s.name, ($base + $s.va + $i - $s.raw), (($b[($i - 8)..($i - 1)] | ForEach-Object { $_.ToString('X2') }) -join ' '),
                (($b[$i..($i + 3)] | ForEach-Object { $_.ToString('X2') }) -join ' '), (($b[($i + 4)..($i + 9)] | ForEach-Object { $_.ToString('X2') }) -join ' ')
        }
    }
}
