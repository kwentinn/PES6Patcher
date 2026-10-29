# Save a screenshot of a process's main window (physical pixels).
# Usage: capture-window.ps1 -Process PES6Patcher -Out shot.png
param([string]$Process, [string]$Out)

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class WinCap {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
[void][WinCap]::SetProcessDpiAwarenessContext([IntPtr]-4)
$h = (Get-Process $Process -ErrorAction Stop | Select-Object -First 1).MainWindowHandle
[void][WinCap]::SetForegroundWindow($h); Start-Sleep -Milliseconds 800
$r = New-Object WinCap+RECT; [void][WinCap]::DwmGetWindowAttribute($h, 9, [ref]$r, 16)   # DWMWA_EXTENDED_FRAME_BOUNDS
$w = $r.R - $r.L; $hh = $r.B - $r.T
$bmp = New-Object Drawing.Bitmap $w, $hh
[Drawing.Graphics]::FromImage($bmp).CopyFromScreen($r.L, $r.T, 0, 0, (New-Object Drawing.Size $w, $hh))
$bmp.Save($Out, [Drawing.Imaging.ImageFormat]::Png)
"{0} x {1} -> {2}" -f $w, $hh, $Out
