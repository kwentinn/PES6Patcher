# Save a screenshot of a process's main window (physical pixels), even if it is behind other windows.
# Usage: capture-window.ps1 -Process PES6Patcher -Out shot.png
param([string]$Process, [string]$Out)

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class WinCap {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
}
'@
[void][WinCap]::SetProcessDpiAwarenessContext([IntPtr]-4)
$h = (Get-Process $Process -ErrorAction Stop | Select-Object -First 1).MainWindowHandle
$r = New-Object WinCap+RECT; [void][WinCap]::GetWindowRect($h, [ref]$r)
$w = $r.R - $r.L; $hh = $r.B - $r.T
$bmp = New-Object Drawing.Bitmap $w, $hh
$g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
[void][WinCap]::PrintWindow($h, $hdc, 2)   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($Out, [Drawing.Imaging.ImageFormat]::Png)
"{0} x {1} -> {2}" -f $w, $hh, $Out
