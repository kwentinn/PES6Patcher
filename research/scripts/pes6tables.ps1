# Set the int16 width of the 512x448 projection templates (and the live struct) in a
# running PES6 process (memory only).
# Usage: pes6tables.ps1 -Width 796   /   pes6tables.ps1 -Width 512 (restore)
param([int]$Width = 796)

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MemTab {
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);
}
'@

# int16 width fields followed by int16 height 448
$Fields = 0xD4FB98, 0xD4FBD0, 0xD4FC08, 0xD4FC40, 0xD4FC78, 0xD4FCB0, 0xD4FCE8, 0xD4FD20, 0x3BECC84

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
$h = [MemTab]::OpenProcess(0x0438, $false, $p.Id)

foreach ($a in $Fields) {
    $b = New-Object byte[] 4; $n = 0
    [void][MemTab]::ReadProcessMemory($h, [IntPtr]$a, $b, 4, [ref]$n)
    $w = [BitConverter]::ToInt16($b, 0); $hh = [BitConverter]::ToInt16($b, 2)
    if ($hh -ne 448) { "0x{0:X}: ignore (hauteur {1})" -f $a, $hh; continue }
    [void][MemTab]::WriteProcessMemory($h, [IntPtr]$a, [BitConverter]::GetBytes([int16]$Width), 2, [ref]$n)
    "0x{0:X}: {1} -> {2}" -f $a, $w, $Width
}
