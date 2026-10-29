# Point the absolute memory operand of the given instructions (currently reading From)
# at To, in a running PES6 process (memory only). Run again with From/To swapped to undo.
# Usage: pes6retarget.ps1 -From 0xB8AD44 -To 0x97E0000 -Instructions 0x4DD8C6,0x4DDFC1
param([int]$From, [int]$To, [int[]]$Instructions)

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MemRt {
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);
    [DllImport("kernel32.dll")] public static extern bool VirtualProtectEx(IntPtr h, IntPtr addr, UIntPtr size, uint prot, out uint old);
}
'@

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
$h = [MemRt]::OpenProcess(0x0438, $false, $p.Id)

$done = 0; $skipped = @()
foreach ($ins in $Instructions) {
    $b = New-Object byte[] 8; $n = 0
    [void][MemRt]::ReadProcessMemory($h, [IntPtr]$ins, $b, 8, [ref]$n)
    $at = -1
    for ($i = 1; $i -le 4; $i++) { if ([BitConverter]::ToInt32($b, $i) -eq $From) { $at = $ins + $i; break } }
    if ($at -lt 0) { $skipped += ('0x{0:X}' -f $ins); continue }
    $old = 0
    [void][MemRt]::VirtualProtectEx($h, [IntPtr]$at, [UIntPtr]4, 0x40, [ref]$old)
    [void][MemRt]::WriteProcessMemory($h, [IntPtr]$at, [BitConverter]::GetBytes($To), 4, [ref]$n)
    [void][MemRt]::VirtualProtectEx($h, [IntPtr]$at, [UIntPtr]4, $old, [ref]$old)
    $done++
}
"{0} instructions redirigees vers 0x{1:X}" -f $done, $To
if ($skipped) { "ignorees : " + ($skipped -join ' ') }
