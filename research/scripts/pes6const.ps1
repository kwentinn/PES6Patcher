# Patch read-only float constants of a running PES6 process (memory only).
# Usage: pes6const.ps1                      -> print the constants
#        pes6const.ps1 -Half 398            -> 256 -> 398 and 1/512 -> 1/796
#        pes6const.ps1 -Half 256            -> restore
param([int]$Half = 0)

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Mem3 {
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);
    [DllImport("kernel32.dll")] public static extern bool VirtualProtectEx(IntPtr h, IntPtr addr, UIntPtr size, uint prot, out uint old);
}
'@

$HalfAddr = 0x00B8AD44  # float 256.0 (half of the 512 virtual width)
$InvAddr  = 0x00B84978  # float 1/512

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
$h = [Mem3]::OpenProcess(0x0438, $false, $p.Id)

function ReadF([int]$addr) { $b = New-Object byte[] 4; $n = 0; [void][Mem3]::ReadProcessMemory($h, [IntPtr]$addr, $b, 4, [ref]$n); [BitConverter]::ToSingle($b, 0) }
function WriteF([int]$addr, [single]$v) {
    $old = 0; $n = 0
    [void][Mem3]::VirtualProtectEx($h, [IntPtr]$addr, [UIntPtr]4, 0x04, [ref]$old)   # PAGE_READWRITE
    $ok = [Mem3]::WriteProcessMemory($h, [IntPtr]$addr, [BitConverter]::GetBytes($v), 4, [ref]$n)
    [void][Mem3]::VirtualProtectEx($h, [IntPtr]$addr, [UIntPtr]4, $old, [ref]$old)
    if (-not $ok) { throw ("ecriture impossible a 0x{0:X}" -f $addr) }
}

"256   (0x{0:X}) = {1}" -f $HalfAddr, (ReadF $HalfAddr)
"1/512 (0x{0:X}) = {1}" -f $InvAddr, (ReadF $InvAddr)
if ($Half -gt 0) {
    WriteF $HalfAddr $Half
    WriteF $InvAddr (1.0 / (2 * $Half))
    "--- apres"
    "demi-largeur = {0}, inverse = {1}" -f (ReadF $HalfAddr), (ReadF $InvAddr)
}
