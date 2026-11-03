# Beep-guided log of the ball carrier while dribbling: one CSV line each time a watched field changes
# (carrier, stick angle and magnitude, state, timer, animation, frame, +0x26, +0x28, heading +0x2C, asked +0x2E,
# running speed, ball direction and speed). Angles in degrees. It showed the 22.5-degree steps of the sprint turns.
# Usage: pes6turnlog.ps1 [-Seconds 30] [-Out turn.csv]
param([int]$Seconds = 30, [string]$Out = "$PSScriptRoot\turn.csv", [int]$StartDelay = 5)

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TurnMem {
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a, bool i, int p);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int s, out int r);
    [DllImport("kernel32.dll")] public static extern bool Beep(int f, int ms);
}
'@

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 is not running."; exit 1 }
$h = [TurnMem]::OpenProcess(0x10, $false, $p.Id)
function Read([int]$a, [int]$n) { $b = New-Object byte[] $n; $r = 0; [void][TurnMem]::ReadProcessMemory($h, [IntPtr]$a, $b, $n, [ref]$r); ,$b }
function Deg([int]$v) { [math]::Round($v * 360.0 / 65536) }

Start-Sleep -Seconds $StartDelay
[void][TurnMem]::Beep(1200, 300)
$sw = [Diagnostics.Stopwatch]::StartNew()
$rows = New-Object System.Collections.Generic.List[string]
$rows.Add('ms,carrier,stick,mag,state,timer,anim,frame,f26,f28,head2C,ask2E,speed,ballDir,ballSpeed')
$last = ''
while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
    $carrier = (Read 0x3BE09CC 1)[0]
    $pad = Read 0x1016680 4
    $ballPtr = [BitConverter]::ToInt32((Read 0xBCCE94 4), 0)
    $ball = Read ($ballPtr + 0x50) 12
    if ($carrier -ge 1 -and $carrier -le 22) {
        $pl = Read (0x3BDC980 + $carrier * 0x240) 0x60
        $animPtr = [BitConverter]::ToInt32($pl, 4)
        $anim = Read ($animPtr + 0x30) 8
        $key = '{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12}' -f $carrier, (Deg ([BitConverter]::ToUInt16($pad, 0))), $pad[2],
            $pl[0x16], [BitConverter]::ToUInt16($pl, 0x18), [BitConverter]::ToUInt16($anim, 0), [BitConverter]::ToUInt16($anim, 4),
            (Deg ([BitConverter]::ToUInt16($pl, 0x26))), (Deg ([BitConverter]::ToUInt16($pl, 0x28))), (Deg ([BitConverter]::ToUInt16($pl, 0x2C))),
            (Deg ([BitConverter]::ToUInt16($pl, 0x2E))), [BitConverter]::ToInt32($pl, 0x1C), (Deg ([BitConverter]::ToUInt16($ball, 8)))
        if ($key -ne $last) { $rows.Add("$($sw.ElapsedMilliseconds),$key,$([BitConverter]::ToInt32($ball, 0))"); $last = $key }
    }
}
[void][TurnMem]::Beep(600, 700)
[IO.File]::WriteAllLines($Out, $rows)
"$($rows.Count - 1) rows -> $Out"
