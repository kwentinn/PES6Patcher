# Read or change the 2D view rectangles of a running PES6 process (memory only).
# Usage: pes6rect.ps1             -> print the rectangles
#        pes6rect.ps1 -Width 796  -> widen them to Width x 448, centred
param([int]$Width = 0)

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Mem2 {
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);
}
'@

$Base = 0x00BEE2D0   # rectangles at D4 (x,y,w,h) and E4 (x,y,w,h), plus F4..FC

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
$h = [Mem2]::OpenProcess(0x0438, $false, $p.Id)

function Dump {
    $buf = New-Object byte[] 0x40; $n = 0
    [void][Mem2]::ReadProcessMemory($h, [IntPtr]$Base, $buf, $buf.Length, [ref]$n)
    for ($o = 0; $o -lt $buf.Length; $o += 4) { "0x{0:X}: {1}" -f ($Base + $o), [BitConverter]::ToSingle($buf, $o) }
}

function WriteF([int]$addr, [single]$v) {
    $n = 0; [void][Mem2]::WriteProcessMemory($h, [IntPtr]$addr, [BitConverter]::GetBytes($v), 4, [ref]$n)
}

Dump
if ($Width -gt 0) {
    foreach ($r in 0xBEE2D4, 0xBEE2E4) {
        WriteF $r (-$Width / 2)
        WriteF ($r + 8) $Width
    }
    "--- apres"
    Dump
}
