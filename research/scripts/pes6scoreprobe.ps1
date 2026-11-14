# Finds the score counters of a match in memory: samples a region of the game's data every -Every ms and records each
# 32-bit value that goes up by exactly 1 (to at most 15) and never does anything else, with the ball's position at that moment.
# Goals make two such counters move (the scores); shots and corners make others, but only a goal happens with the ball in the net.
# Usage: pes6scoreprobe.ps1 -Seconds 600 [-SkipScan 1] [-SpeedPatch speed-x2] [-Out score-candidates.txt]
# Goal sequences wait for Start: with -SkipScan (1 = Esc), the key is sent when the ball sits in a goal (|x| > 13500, |z| < 1500) and
# nobody moves for 3 s, at most every 4 s, with the game brought to the front. 0 turns that off. Everything is put back at the end.
param([int]$Seconds = 600, [int]$Every = 400, [int]$Start = 0x3B80000, [int]$Length = 0x100000, [int]$SkipScan = 1,
      [string]$SpeedPatch = 'speed-x2', [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) 'pes6-score-candidates.txt'))

Add-Type @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class SP {
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a, bool i, int p);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int s, out int r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr w);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public static Dictionary<int, int> Inc = new Dictionary<int, int>(), Bad = new Dictionary<int, int>();
    public static List<string> Events = new List<string>();
    // compares two snapshots dword by dword
    public static void Diff(byte[] prev, byte[] cur, int baseAddr, double bx, double bz, double t) {
        for (int i = 0; i + 4 <= cur.Length; i += 4) {
            int p = BitConverter.ToInt32(prev, i), c = BitConverter.ToInt32(cur, i);
            if (p == c) continue;
            int a = baseAddr + i;
            if (c == p + 1 && c >= 1 && c <= 15) {
                int n; Inc.TryGetValue(a, out n); Inc[a] = n + 1;
                Events.Add(string.Format("t={0,6:F1}  0x{1:X}: {2} -> {3}   ball x={4:F0} z={5:F0}", t, a, p, c, bx, bz));
            } else { int n; Bad.TryGetValue(a, out n); Bad[a] = n + 1; }
        }
    }
}
'@
$patchTool = Join-Path $PSScriptRoot 'pes6patch.ps1'
$game = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $game) { Write-Error 'PES6 is not running.'; exit 1 }
$h = [SP]::OpenProcess(0x10, $false, $game.Id)
function ReadMem([int]$a, [int]$n) { $b = New-Object byte[] $n; $r = 0; [void][SP]::ReadProcessMemory($h, [IntPtr]$a, $b, $n, [ref]$r); ,$b }
function Press-Skip {
    [SP]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [SP]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [void][SP]::SetForegroundWindow($game.MainWindowHandle); Start-Sleep -Milliseconds 150
    [SP]::keybd_event(0, [byte]$SkipScan, 0x8, [UIntPtr]::Zero); Start-Sleep -Milliseconds 90
    [SP]::keybd_event(0, [byte]$SkipScan, 0xA, [UIntPtr]::Zero)
}
if ($SpeedPatch) { & $patchTool -Apply $SpeedPatch -Force }
$presses = New-Object System.Collections.Generic.List[string]
$sw = [Diagnostics.Stopwatch]::StartNew(); $prev = ReadMem $Start $Length; $prevPl = ReadMem (0x3BDC980 + 0x240) (22 * 0x240); $frozen = $null; $last = -100
try {
    while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
        $t0 = $sw.ElapsedMilliseconds; $t = $t0 / 1000.0
        $bp = [BitConverter]::ToInt32((ReadMem 0xBCCE94 4), 0); $b = ReadMem ($bp + 0x10) 12
        $bx = [BitConverter]::ToSingle($b, 0); $bz = [BitConverter]::ToSingle($b, 8)
        $cur = ReadMem $Start $Length
        [SP]::Diff($prev, $cur, $Start, $bx, $bz, $t); $prev = $cur
        $pl = ReadMem (0x3BDC980 + 0x240) (22 * 0x240); $moved = 0
        for ($i = 0; $i -lt 22; $i++) { $moved += [math]::Abs([BitConverter]::ToSingle($pl, $i * 0x240 + 0xE0) - [BitConverter]::ToSingle($prevPl, $i * 0x240 + 0xE0)) }
        $prevPl = $pl
        if ($moved -lt 20) { if (-not $frozen) { $frozen = $t } } else { $frozen = $null }
        $inNet = [math]::Abs($bx) -gt 13500 -and [math]::Abs($bz) -lt 1500
        if ($SkipScan -gt 0 -and $inNet -and $frozen -and ($t - $frozen) -gt 3 -and ($t - $last) -gt 4) {
            Press-Skip; $last = $t; $presses.Add(("t={0:F1} skip key, ball x={1:F0} z={2:F0}" -f $t, $bx, $bz))
        }
        if (-not (Get-Process -Id $game.Id -ErrorAction SilentlyContinue)) { break }
        $left = $Every - ($sw.ElapsedMilliseconds - $t0); if ($left -gt 0) { Start-Sleep -Milliseconds $left }
    }
}
finally { if ($SpeedPatch -and (Get-Process PES6 -ErrorAction SilentlyContinue)) { & $patchTool -Revert $SpeedPatch -Force } }

$lines = @("score probe: $([int]$sw.Elapsed.TotalSeconds) s, region 0x$('{0:X}' -f $Start) + 0x$('{0:X}' -f $Length), skip key pressed $($presses.Count) time(s)")
$lines += $presses
$lines += 'Candidates (only ever went up by 1, never anything else), by number of increments:'
foreach ($a in ([SP]::Inc.Keys | Where-Object { -not [SP]::Bad.ContainsKey($_) } | Sort-Object { [SP]::Inc[$_] } -Descending | Select-Object -First 40)) { $lines += ('  0x{0:X}: {1} increment(s)' -f $a, [SP]::Inc[$a]) }
$lines += 'Increments of those candidates, with the ball:'
$good = [SP]::Inc.Keys | Where-Object { -not [SP]::Bad.ContainsKey($_) }
foreach ($e in [SP]::Events) { foreach ($a in $good) { if ($e -like ('*0x{0:X}:*' -f $a)) { $lines += $e; break } } }
$lines | Set-Content $Out
$lines
