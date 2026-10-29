# Dump a memory range of a running PES6 process as int16 / int32 / float columns.
# Usage: pes6dump.ps1 -Address 0x3BECC60 -Length 0x50
param([int]$Address, [int]$Length = 0x40)

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MemDump {
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
}
'@

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
$h = [MemDump]::OpenProcess(0x10, $false, $p.Id)
$buf = New-Object byte[] $Length; $n = 0
if (-not [MemDump]::ReadProcessMemory($h, [IntPtr]$Address, $buf, $Length, [ref]$n)) { Write-Error "lecture impossible"; exit 1 }
for ($o = 0; $o -lt $Length; $o += 4) {
    "0x{0:X8}: i16={1,6} {2,6}  i32={3,11}  f={4}" -f ($Address + $o),
        [BitConverter]::ToInt16($buf, $o), [BitConverter]::ToInt16($buf, $o + 2),
        [BitConverter]::ToInt32($buf, $o), [BitConverter]::ToSingle($buf, $o)
}
