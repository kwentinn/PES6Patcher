# Logs the ball carrier and the ball every ~10 ms: positions, the player's actual run direction (from the
# position change), facing +0x26, move +0x28, heading +0x2C, stick, ball direction, distance. Beeps at start and end.
param([int]$Seconds = 30, [string]$Out, [int]$StartDelay = 3)
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class DL { [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a,bool i,int p);
[DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h,IntPtr a,byte[] b,int s,out int r);
[DllImport("kernel32.dll")] public static extern bool Beep(int f, int ms); }
'@
$h = [DL]::OpenProcess(0x10, $false, (Get-Process PES6 | Select-Object -First 1).Id)
function ReadMem([int]$a, [int]$n) { $b = New-Object byte[] $n; $r = 0; [void][DL]::ReadProcessMemory($h, [IntPtr]$a, $b, $n, [ref]$r); ,$b }
function Deg([int]$v) { [math]::Round($v * 360.0 / 65536) }
Start-Sleep -Seconds $StartDelay
[void][DL]::Beep(1200, 300)
$rows = New-Object System.Collections.Generic.List[string]
$rows.Add('ms,carrier,state,anim,stick,mag,f26,f28,h2C,runDir,speed,px,pz,bx,bz,ballDir,ballSpeed,dist')
$sw = [Diagnostics.Stopwatch]::StartNew(); $lpx = 0.0; $lpz = 0.0; $lc = -1
while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
    $c = (ReadMem 0x3BE09CC 1)[0]
    if ($c -ge 1 -and $c -le 22) {
        $pl = ReadMem (0x3BDC980 + $c * 0x240) 0xF0
        $anim = ReadMem ([BitConverter]::ToInt32($pl, 4) + 0x30) 2
        $pad = ReadMem 0x1016680 4
        $bp = [BitConverter]::ToInt32((ReadMem 0xBCCE94 4), 0); $bl = ReadMem ($bp + 0x10) 0x50
        $px = [BitConverter]::ToSingle($pl, 0xE0); $pz = [BitConverter]::ToSingle($pl, 0xE8)
        $bx = [BitConverter]::ToSingle($bl, 0); $bz = [BitConverter]::ToSingle($bl, 8)
        $run = ''
        if ($c -eq $lc -and (($px - $lpx) -ne 0 -or ($pz - $lpz) -ne 0)) { $run = [math]::Round([math]::Atan2($pz - $lpz, $px - $lpx) * 180 / [math]::PI) }
        $key = '{0},{1},{2},{3},{4},{5},{6},{7},{8}' -f $c, $pl[0x16], [BitConverter]::ToUInt16($anim, 0), (Deg ([BitConverter]::ToUInt16($pad, 0))), $pad[2],
            (Deg ([BitConverter]::ToUInt16($pl, 0x26))), (Deg ([BitConverter]::ToUInt16($pl, 0x28))), (Deg ([BitConverter]::ToUInt16($pl, 0x2C))), $run
        if ($px -ne $lpx -or $pz -ne $lpz) {
            $rows.Add(('{0},{1},{2},{3:F0},{4:F0},{5:F0},{6:F0},{7},{8},{9:F0}' -f [int]$sw.ElapsedMilliseconds, $key, [BitConverter]::ToInt32($pl, 0x1C), $px, $pz, $bx, $bz,
                (Deg ([BitConverter]::ToUInt16($bl, 0x48))), [BitConverter]::ToInt32($bl, 0x40), [math]::Sqrt(($bx - $px) * ($bx - $px) + ($bz - $pz) * ($bz - $pz))))
        }
        $lpx = $px; $lpz = $pz; $lc = $c
    }
    Start-Sleep -Milliseconds 5
}
[void][DL]::Beep(600, 700)
$rows | Set-Content $Out
"{0} rows -> {1}" -f ($rows.Count - 1), $Out
