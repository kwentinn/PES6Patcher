# Read or overwrite bytes of a running PES6 process (memory only), e.g. code.
# Usage: pes6code.ps1 -Address 0x8744F0 -Length 8
#        pes6code.ps1 -Address 0x8744F1 -Hex "1C 03 00 00"
param([int]$Address, [int]$Length = 16, [string]$Hex = "")

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MemCode {
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);
    [DllImport("kernel32.dll")] public static extern bool VirtualProtectEx(IntPtr h, IntPtr addr, UIntPtr size, uint prot, out uint old);
}
'@

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
$h = [MemCode]::OpenProcess(0x0438, $false, $p.Id)

if ($Hex) {
    $bytes = [byte[]]($Hex -split '\s+' | Where-Object { $_ } | ForEach-Object { [Convert]::ToByte($_, 16) })
    $old = 0; $n = 0
    [void][MemCode]::VirtualProtectEx($h, [IntPtr]$Address, [UIntPtr]$bytes.Length, 0x40, [ref]$old)
    $ok = [MemCode]::WriteProcessMemory($h, [IntPtr]$Address, $bytes, $bytes.Length, [ref]$n)
    [void][MemCode]::VirtualProtectEx($h, [IntPtr]$Address, [UIntPtr]$bytes.Length, $old, [ref]$old)
    if (-not $ok) { Write-Error "ecriture impossible"; exit 1 }
    $Length = [Math]::Max($Length, $bytes.Length)
}
$buf = New-Object byte[] $Length; $n = 0
[void][MemCode]::ReadProcessMemory($h, [IntPtr]$Address, $buf, $Length, [ref]$n)
"0x{0:X}: {1}" -f $Address, (($buf | ForEach-Object { $_.ToString('X2') }) -join ' ')
