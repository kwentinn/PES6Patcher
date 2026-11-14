# One snapshot of the 22 players (id, team, slot, state, position, target, buttons) and the ball: who carries it, where everybody is.
# Usage: pes6players.ps1
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class SN { [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a,bool i,int p);
[DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h,IntPtr a,byte[] b,int s,out int r); }
'@
$h = [SN]::OpenProcess(0x10, $false, (Get-Process PES6 | Select-Object -First 1).Id)
function ReadMem([int]$a, [int]$n) { $b = New-Object byte[] $n; $r = 0; [void][SN]::ReadProcessMemory($h, [IntPtr]$a, $b, $n, [ref]$r); ,$b }
$c = (ReadMem 0x3BE09CC 1)[0]
$bp = [BitConverter]::ToInt32((ReadMem 0xBCCE94 4), 0); $bl = ReadMem ($bp + 0x10) 0x10
"carrier=$c ball x={0:F0} y={1:F0} z={2:F0}" -f [BitConverter]::ToSingle($bl,0), [BitConverter]::ToSingle($bl,4), [BitConverter]::ToSingle($bl,8)
"id team slot state  px     pz     t6C    t70   buttons"
for ($i = 1; $i -le 22; $i++) {
    $pl = ReadMem (0x3BDC980 + $i * 0x240) 0x240
    "{0,2} {1,4} {2,4} {3,5} {4,6:F0} {5,6:F0} {6,6} {7,6} {8:X8}" -f $i, $pl[0x12], $pl[0x11], $pl[0x16],
        [BitConverter]::ToSingle($pl,0xE0), [BitConverter]::ToSingle($pl,0xE8),
        [BitConverter]::ToInt32($pl,0x6C), [BitConverter]::ToInt32($pl,0x70), [BitConverter]::ToInt32($pl,0xB0)
}
