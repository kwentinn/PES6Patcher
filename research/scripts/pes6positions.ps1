# Logs the 22 players and the ball every ~100 ms: one CSV line per sample and player.
# Usage: pes6positions.ps1 -Seconds 90 -Out positions.csv [-Every 200] [-StartDelay 5]  (beeps at the start and the end when StartDelay is given)
# Columns: ms, carrier, ball x/z, then per player: id (1-11 and 12-22 are the two teams), team, slot (0 = keeper), state, x, z, target x/z, speed.
# Read-only (ReadProcessMemory): safe to run during a whole match; feed the file to pes6depth.ps1.
param([int]$Seconds = 60, [string]$Out, [int]$StartDelay = 0, [int]$Every = 100)
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class PL { [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a,bool i,int p);
[DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h,IntPtr a,byte[] b,int s,out int r);
[DllImport("kernel32.dll")] public static extern bool Beep(int f, int ms); }
'@
$h = [PL]::OpenProcess(0x10, $false, (Get-Process PES6 | Select-Object -First 1).Id)
function ReadMem([int]$a, [int]$n) { $b = New-Object byte[] $n; $r = 0; [void][PL]::ReadProcessMemory($h, [IntPtr]$a, $b, $n, [ref]$r); ,$b }
if ($StartDelay) { Start-Sleep -Seconds $StartDelay; [void][PL]::Beep(1200, 300) }
$rows = New-Object System.Collections.Generic.List[string]
$rows.Add('ms,carrier,bx,bz,id,team,slot,state,px,pz,tx,tz,speed')
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
    $ms = [int]$sw.ElapsedMilliseconds
    $c = (ReadMem 0x3BE09CC 1)[0]
    $bp = [BitConverter]::ToInt32((ReadMem 0xBCCE94 4), 0); $bl = ReadMem ($bp + 0x10) 0x10
    $all = ReadMem (0x3BDC980 + 0x240) (22 * 0x240)
    $bx = [BitConverter]::ToSingle($bl, 0); $bz = [BitConverter]::ToSingle($bl, 8)
    for ($i = 0; $i -lt 22; $i++) {
        $o = $i * 0x240
        $rows.Add(('{0},{1},{2:F0},{3:F0},{4},{5},{6},{7},{8:F0},{9:F0},{10:F0},{11:F0},{12}' -f $ms, $c, $bx, $bz, ($i + 1), $all[$o + 0x12], $all[$o + 0x11], $all[$o + 0x16],
            [BitConverter]::ToSingle($all, $o + 0xE0), [BitConverter]::ToSingle($all, $o + 0xE8),
            [BitConverter]::ToSingle($all, $o + 0x6C), [BitConverter]::ToSingle($all, $o + 0x70), [BitConverter]::ToInt32($all, $o + 0x1C)))
    }
    $left = $Every - ([int]$sw.ElapsedMilliseconds - $ms); if ($left -gt 0) { Start-Sleep -Milliseconds $left }
}
if ($StartDelay) { [void][PL]::Beep(600, 700) }
$rows | Set-Content $Out
"{0} samples -> {1}" -f (($rows.Count - 1) / 22), $Out
