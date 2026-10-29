# Read or change the 2D (menu) virtual width of a running PES6 process.
# Usage: pes6mem.ps1            -> print current values
#        pes6mem.ps1 -Width 796 -> write a new virtual width (memory only, nothing saved to disk)
param([int]$Width = 0)

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Mem {
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
}
'@

$WidthAddr  = 0x03BECC84  # int16: virtual 2D width (512 on PS2)
$HeightAddr = 0x00BA2E1C  # float: virtual 2D height (448)

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
$h = [Mem]::OpenProcess(0x0438, $false, $p.Id)  # VM_READ | VM_WRITE | VM_OPERATION | QUERY_INFORMATION
if ($h -eq [IntPtr]::Zero) { Write-Error "Impossible d'ouvrir le processus (essayer en administrateur)."; exit 1 }

function Read([int]$addr, [int]$size) {
    $buf = New-Object byte[] $size; $n = 0
    if (-not [Mem]::ReadProcessMemory($h, [IntPtr]$addr, $buf, $size, [ref]$n)) { throw ("lecture impossible a 0x{0:X}" -f $addr) }
    $buf
}

"largeur virtuelle = {0}" -f [BitConverter]::ToInt16((Read $WidthAddr 2), 0)
"hauteur virtuelle = {0}" -f [BitConverter]::ToSingle((Read $HeightAddr 4), 0)

if ($Width -gt 0) {
    $n = 0
    if (-not [Mem]::WriteProcessMemory($h, [IntPtr]$WidthAddr, [BitConverter]::GetBytes([int16]$Width), 2, [ref]$n)) { throw "ecriture impossible" }
    "nouvelle largeur  = {0}" -f [BitConverter]::ToInt16((Read $WidthAddr 2), 0)
}
[void][Mem]::CloseHandle($h)
