# Automatic A/B test of a memory patch during a match nobody plays (CPU against CPU), at x2 speed: waits for the match
# to start, then alternates the game as it is (A) and with the patch (B) every -Phase seconds, logging the 22 players and
# the ball the whole time, and prints the back line by ball zone for each. Everything is put back at the end (or on Ctrl+C).
# Usage: pes6ab.ps1 -Patch defence-line [-Phase 90] [-Cycles 3] [-Settle 10] [-SpeedPatch speed-x2] [-Every 250] [-Out ab.csv]
#        Start an exhibition match with both teams on COM, then leave it alone.
# -SpeedPatch '' keeps the game's own speed. -Settle seconds at the start of every phase are left out of the summary
# (the players need a moment to move to their new places). Read-only apart from the named patches (research/patches.json).
param([Parameter(Mandatory)][string]$Patch, [int]$Phase = 90, [int]$Cycles = 3, [int]$Settle = 10, [string]$SpeedPatch = 'speed-x2',
      [int]$Every = 250, [int]$WaitMinutes = 30, [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) 'pes6-ab.csv'))

Add-Type @'
using System; using System.Runtime.InteropServices;
public static class AB { [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a,bool i,int p);
[DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h,IntPtr a,byte[] b,int s,out int r);
[DllImport("kernel32.dll")] public static extern bool Beep(int f, int ms); }
'@
$patchTool = Join-Path $PSScriptRoot 'pes6patch.ps1'
$game = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $game) { Write-Error 'PES6 is not running.'; exit 1 }
$h = [AB]::OpenProcess(0x10, $false, $game.Id)
function ReadMem([int]$a, [int]$n) { $b = New-Object byte[] $n; $r = 0; [void][AB]::ReadProcessMemory($h, [IntPtr]$a, $b, $n, [ref]$r); ,$b }
function Avg($v) { if (@($v).Count) { ($v | Measure-Object -Average).Average } else { [double]::NaN } }
function InMatch {
    # the keepers stand near their goals; in the menus the players array is empty
    $a = [math]::Abs([BitConverter]::ToSingle((ReadMem (0x3BDC980 + 0x240 + 0xE0) 4), 0))
    $b = [math]::Abs([BitConverter]::ToSingle((ReadMem (0x3BDC980 + 12 * 0x240 + 0xE0) 4), 0))
    $a -gt 8000 -and $b -gt 8000
}
function Patch([string]$verb, [string]$name, [switch]$Force) {
    if (-not $name) { return }
    $on = $verb -eq '-Apply'
    if ($Force) { if ($on) { & $patchTool -Apply $name -Force } else { & $patchTool -Revert $name -Force } }
    elseif ($on) { & $patchTool -Apply $name } else { & $patchTool -Revert $name }
}

# 1. wait for a match
Write-Host "Waiting for a match (up to $WaitMinutes min): start CPU against CPU in the game."
$deadline = (Get-Date).AddMinutes($WaitMinutes)
while (-not (InMatch)) {
    if ((Get-Date) -gt $deadline) { Write-Error 'no match started'; exit 1 }
    if (-not (Get-Process -Id $game.Id -ErrorAction SilentlyContinue)) { Write-Error 'the game closed'; exit 1 }
    Start-Sleep -Seconds 2
}
Start-Sleep -Seconds 5   # past the kick-off set-up
[void][AB]::Beep(1200, 300)
Write-Host 'Match found.'

$rows = New-Object System.Collections.Generic.List[object]
$lost = $null
$sw = [Diagnostics.Stopwatch]::StartNew()
try {
    for ($c = 0; $c -lt $Cycles; $c++) {
        foreach ($label in 'A', 'B') {
            if ($label -eq 'B') { Patch '-Apply' $Patch } else { Patch '-Revert' $Patch }
            if ($SpeedPatch) { Patch '-Apply' $SpeedPatch -Force }
            Write-Host ("[{0}] cycle {1}, phase {2}" -f [int]$sw.Elapsed.TotalSeconds, ($c + 1), $label)
            $phaseStart = $sw.Elapsed.TotalSeconds
            while ($sw.Elapsed.TotalSeconds - $phaseStart -lt $Phase) {
                $t0 = $sw.ElapsedMilliseconds
                if (InMatch) { $lost = $null } elseif (-not $lost) { $lost = $sw.Elapsed.TotalSeconds } elseif ($sw.Elapsed.TotalSeconds - $lost -gt 20) { throw 'the match ended' }
                $carrier = (ReadMem 0x3BE09CC 1)[0]
                $bp = [BitConverter]::ToInt32((ReadMem 0xBCCE94 4), 0); $bx = [BitConverter]::ToSingle((ReadMem ($bp + 0x10) 4), 0)
                $all = ReadMem (0x3BDC980 + 0x240) (22 * 0x240)
                $words = ReadMem 0x3BCDCAE 4
                $step = [BitConverter]::ToSingle((ReadMem 0x3BE1740 4), 0)
                $settled = ($sw.Elapsed.TotalSeconds - $phaseStart) -ge $Settle
                foreach ($t in 0, 1) {
                    $kx = [BitConverter]::ToSingle($all, $t * 11 * 0x240 + 0xE0)
                    $d = if ($kx -lt 0) { 1 } else { -1 }
                    $xs = for ($i = 1; $i -le 10; $i++) { $d * [BitConverter]::ToSingle($all, ($t * 11 + $i) * 0x240 + 0xE0) }
                    $back = Avg ($xs | Sort-Object | Select-Object -First 4)
                    $side = $all[$t * 11 * 0x240 + 0x13]
                    $poss = if ($carrier -lt 1 -or $carrier -gt 22) { 'none' } elseif (($carrier -le 11) -eq ($t -eq 0)) { 'own' } else { 'opp' }
                    $rows.Add([pscustomobject]@{ ms = $t0; cycle = $c + 1; label = $label; settled = $settled; team = $t; poss = $poss; ball = $d * $bx
                        back4 = $back; lineWord = $d * [BitConverter]::ToInt16($words, 2 * $side); step = $step })
                }
                $left = $Every - ($sw.ElapsedMilliseconds - $t0); if ($left -gt 0) { Start-Sleep -Milliseconds $left }
            }
        }
    }
}
catch { Write-Host "Stopped: $($_.Exception.Message)" -ForegroundColor Yellow }
finally {
    if ($Patch -and (Get-Process PES6 -ErrorAction SilentlyContinue)) { Patch '-Revert' $Patch; if ($SpeedPatch) { Patch '-Revert' $SpeedPatch -Force } }
    [void][AB]::Beep(600, 700)
}

# 3. summary
$rows | Export-Csv $Out -NoTypeInformation
$use = @($rows | Where-Object { $_.settled -and $_.poss -eq 'opp' })
"{0} samples ({1} used: the side without the ball, settled) -> {2}" -f ($rows.Count / 2), $use.Count, $Out
"game step seen: " + (($rows.step | Sort-Object -Unique) -join ', ')
"Back four (mean x of the 4 deepest outfielders) and line word, own goal at -12400, by ball zone:"
$zones = $use | Group-Object { [math]::Floor($_.ball / 4000) * 4000 } | Sort-Object { [int]$_.Name }
foreach ($z in $zones) {
    $a = @($z.Group | Where-Object label -eq 'A'); $b = @($z.Group | Where-Object label -eq 'B')
    'ball {0,6}..{1,6}:  A back4={2,6:F0} line={3,6:F0} (n={4,3})   B back4={5,6:F0} line={6,6:F0} (n={7,3})   B-A back4={8,5:F0} line={9,5:F0}' -f [int]$z.Name, ([int]$z.Name + 4000),
        (Avg $a.back4), (Avg $a.lineWord), $a.Count, (Avg $b.back4), (Avg $b.lineWord), $b.Count, ((Avg $b.back4) - (Avg $a.back4)), ((Avg $b.lineWord) - (Avg $a.lineWord))
}
