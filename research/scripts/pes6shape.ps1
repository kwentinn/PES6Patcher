# Team shape while not in possession: samples the 22 players, then prints, by ball zone and in each side's own
# direction (0 = centre, negative = own half): mean x of the defenders (role +0x10 1-7), midfielders (8-0x17) and
# forwards (0x18+), the gaps between those lines, the block length and width, and the spread of each line.
# Usage: pes6shape.ps1 -Seconds 90 [-Out shape.csv]   (read-only; the line is only worth reading while the opponent has the ball)
param([int]$Seconds = 90, [int]$Every = 250, [string]$Out)
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class SH { [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a,bool i,int p);
[DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h,IntPtr a,byte[] b,int s,out int r); }
'@
$h = [SH]::OpenProcess(0x10, $false, (Get-Process PES6 | Select-Object -First 1).Id)
function ReadMem([int]$a, [int]$n) { $b = New-Object byte[] $n; $r = 0; [void][SH]::ReadProcessMemory($h, [IntPtr]$a, $b, $n, [ref]$r); ,$b }
function Avg($v) { if (@($v).Count) { ($v | Measure-Object -Average).Average } else { [double]::NaN } }
function Sd($v) { $v = @($v); if ($v.Count -lt 2) { return 0 }; $m = Avg $v; [math]::Sqrt((($v | ForEach-Object { ($_ - $m) * ($_ - $m) }) | Measure-Object -Sum).Sum / $v.Count) }
$o = New-Object System.Collections.Generic.List[object]
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
    $ms = [int]$sw.ElapsedMilliseconds
    $c = (ReadMem 0x3BE09CC 1)[0]
    $bp = [BitConverter]::ToInt32((ReadMem 0xBCCE94 4), 0); $bx = [BitConverter]::ToSingle((ReadMem ($bp + 0x10) 4), 0)
    $all = ReadMem (0x3BDC980 + 0x240) (22 * 0x240)
    foreach ($t in 0, 1) {
        $kx = [BitConverter]::ToSingle($all, $t * 11 * 0x240 + 0xE0); if ($kx -eq 0) { continue }
        $d = if ($kx -lt 0) { 1 } else { -1 }
        $poss = if ($c -lt 1 -or $c -gt 22) { 'none' } elseif (($c -le 11) -eq ($t -eq 0)) { 'own' } else { 'opp' }
        $def = @(); $mid = @(); $fwd = @(); $xs = @(); $zs = @(); $roles = @()
        for ($i = 1; $i -le 10; $i++) { $p = ($t * 11 + $i) * 0x240
            $x = $d * [BitConverter]::ToSingle($all, $p + 0xE0); $xs += $x; $zs += [BitConverter]::ToSingle($all, $p + 0xE8); $r = $all[$p + 0x10]; $roles += $r
            if ($r -le 7) { $def += $x } elseif ($r -lt 0x18) { $mid += $x } else { $fwd += $x } }
        $s = $xs | Sort-Object; $z = $zs | Sort-Object
        $o.Add([pscustomobject]@{ poss = $poss; ball = $d * $bx; def = Avg $def; mid = Avg $mid; fwd = Avg $fwd; sdDef = Sd $def; sdMid = Sd $mid
            length = $s[9] - $s[0]; width = $z[9] - $z[0]; roles = ($roles | ForEach-Object { '{0:X}' -f $_ }) -join ' ' })
    }
    $left = $Every - ([int]$sw.ElapsedMilliseconds - $ms); if ($left -gt 0) { Start-Sleep -Milliseconds $left }
}
if ($Out) { $o | Export-Csv $Out }
"samples: $($o.Count / 2); roles seen: " + (($o.roles | Sort-Object -Unique | Select-Object -First 4) -join ' | ')
$o | Where-Object { $_.poss -eq 'opp' } | Group-Object { [math]::Floor($_.ball / 4000) * 4000 } | Sort-Object { [int]$_.Name } | ForEach-Object {
    $m = $_.Group
    'ball {0,6}..{1,6}: def={2,6:F0} mid={3,6:F0} fwd={4,6:F0} | mid-def={5,5:F0} fwd-mid={6,5:F0} | length={7,5:F0} width={8,5:F0} | sdDef={9,4:F0} sdMid={10,4:F0} n={11}' -f [int]$_.Name, ([int]$_.Name + 4000),
        (Avg $m.def), (Avg $m.mid), (Avg $m.fwd), ((Avg $m.mid) - (Avg $m.def)), ((Avg $m.fwd) - (Avg $m.mid)), (Avg $m.length), (Avg $m.width), (Avg $m.sdDef), (Avg $m.sdMid), $m.Count
}
