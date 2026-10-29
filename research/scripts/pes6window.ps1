# Report the PES6 window geometry and DPI handling while the game runs (read-only).
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class WinInfo {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
}
'@
[void][WinInfo]::SetProcessDpiAwarenessContext([IntPtr]-4)   # per-monitor v2: read physical pixels

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
$found = New-Object System.Collections.ArrayList
$cb = [WinInfo+EnumProc]{ param($h, $x) $wp = 0; [void][WinInfo]::GetWindowThreadProcessId($h, [ref]$wp); if ($wp -eq $p.Id -and [WinInfo]::IsWindowVisible($h)) { [void]$found.Add($h) }; $true }
[void][WinInfo]::EnumWindows($cb, [IntPtr]::Zero)
foreach ($h in $found) {
    $t = New-Object Text.StringBuilder 256; [void][WinInfo]::GetWindowText($h, $t, 256)
    $w = New-Object WinInfo+RECT; $c = New-Object WinInfo+RECT; $d = New-Object WinInfo+RECT
    [void][WinInfo]::GetWindowRect($h, [ref]$w); [void][WinInfo]::GetClientRect($h, [ref]$c)
    [void][WinInfo]::DwmGetWindowAttribute($h, 9, [ref]$d, 16)
    $aw = [WinInfo]::GetAwarenessFromDpiAwarenessContext([WinInfo]::GetWindowDpiAwarenessContext($h))
    "fenetre '{0}'" -f $t
    "  rect (physique DWM) : {0},{1} -> {2},{3}  ({4} x {5})" -f $d.L, $d.T, $d.R, $d.B, ($d.R - $d.L), ($d.B - $d.T)
    "  rect (GetWindowRect): {0},{1} -> {2},{3}  ({4} x {5})" -f $w.L, $w.T, $w.R, $w.B, ($w.R - $w.L), ($w.B - $w.T)
    "  zone client         : {0} x {1}" -f ($c.R - $c.L), ($c.B - $c.T)
    "  DPI fenetre = {0} ; prise en compte DPI = {1} (0 = non, 1 = systeme, 2 = par ecran)" -f [WinInfo]::GetDpiForWindow($h), $aw
}
