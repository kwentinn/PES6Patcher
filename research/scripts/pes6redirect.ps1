# Redirect selected instructions of a running PES6 process from the shared 256 / 1/512
# constants to private copies (memory only), so only the 2D drawing code is widened.
# Usage: pes6redirect.ps1 -Half 398      -> apply (16:9: 398, 32:9: 796)
#        pes6redirect.ps1 -Revert        -> put the original operands back
param([int]$Half = 398, [switch]$Revert)

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Mem4 {
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);
    [DllImport("kernel32.dll")] public static extern bool VirtualProtectEx(IntPtr h, IntPtr addr, UIntPtr size, uint prot, out uint old);
    [DllImport("kernel32.dll")] public static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr addr, UIntPtr size, uint type, uint prot);
}
'@

$HalfConst = 0x00B8AD44   # 256.0, shared with gameplay code
$InvConst  = 0x00B84978   # 1/512, shared with 3D normal unpacking

# Readers of 256 in the graphics code (0x656000 and above); gameplay readers stay untouched.
$HalfReaders = 0x656AF7, 0x656BD9, 0x656F0D, 0x657A2E, 0x6D1AE9,
               0x78ED90, 0x78F038, 0x78F0D7,
               0x867BE2, 0x867C0B, 0x867C40, 0x867C69,
               0x87AEDC, 0x8B6E9D, 0x8B6EC9,
               0x8DBAA0, 0x8DC270, 0x8DC319, 0x8DC3F8,
               0x922EA5, 0x941B98
# Readers of 1/512 that convert 2D coordinates (not the normal unpackers at 0x437xxx).
$InvReaders = 0x87AEE4, 0x656821

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
$h = [Mem4]::OpenProcess(0x0438, $false, $p.Id)

function ReadB([int]$addr, [int]$n) { $b = New-Object byte[] $n; $r = 0; [void][Mem4]::ReadProcessMemory($h, [IntPtr]$addr, $b, $n, [ref]$r); $b }
function WriteB([int]$addr, [byte[]]$bytes) {
    $old = 0; $w = 0
    [void][Mem4]::VirtualProtectEx($h, [IntPtr]$addr, [UIntPtr]$bytes.Length, 0x40, [ref]$old)  # PAGE_EXECUTE_READWRITE
    $ok = [Mem4]::WriteProcessMemory($h, [IntPtr]$addr, $bytes, $bytes.Length, [ref]$w)
    [void][Mem4]::VirtualProtectEx($h, [IntPtr]$addr, [UIntPtr]$bytes.Length, $old, [ref]$old)
    if (-not $ok) { throw ("ecriture impossible a 0x{0:X}" -f $addr) }
}

# Find the 4-byte absolute operand inside the instruction at $ins that currently points to one of $targets.
function FindOperand([int]$ins, [int[]]$targets) {
    $b = ReadB $ins 8
    for ($i = 1; $i -le 4; $i++) {
        $v = [BitConverter]::ToInt32($b, $i)
        if ($targets -contains $v) { return @{ Addr = $ins + $i; Value = $v } }
    }
    $null
}

$newHalf = 0; $newInv = 0
if (-not $Revert) {
    $mem = [Mem4]::VirtualAllocEx($h, [IntPtr]::Zero, [UIntPtr]8, 0x3000, 0x04)  # MEM_COMMIT|MEM_RESERVE, PAGE_READWRITE
    if ($mem -eq [IntPtr]::Zero) { throw "allocation impossible" }
    $newHalf = [int]$mem; $newInv = $newHalf + 4
    WriteB $newHalf ([BitConverter]::GetBytes([single]$Half))
    WriteB $newInv  ([BitConverter]::GetBytes([single](1.0 / (2 * $Half))))
    "constantes privees a 0x{0:X}: {1} et 1/{2}" -f $newHalf, $Half, (2 * $Half)
}

$done = 0; $skipped = @()
foreach ($pair in @(@($HalfReaders, $HalfConst), @($InvReaders, $InvConst))) {
    foreach ($ins in $pair[0]) {
        if ($Revert) {
            # The operand currently points somewhere else; locate it by its position next to the opcode.
            $b = ReadB $ins 8; $hit = $null
            for ($i = 1; $i -le 4; $i++) { $v = [BitConverter]::ToInt32($b, $i); if ($v -gt 0x10000 -and $v -ne $pair[1] -and ($v -lt 0x400000 -or $v -gt 0x5000000)) { $hit = $ins + $i; break } }
            if ($hit) { WriteB $hit ([BitConverter]::GetBytes([int]$pair[1])); $done++ } else { $skipped += ('0x{0:X}' -f $ins) }
        } else {
            $op = FindOperand $ins @($pair[1])
            if (-not $op) { $skipped += ('0x{0:X}' -f $ins); continue }
            $dest = if ($pair[1] -eq $HalfConst) { $newHalf } else { $newInv }
            WriteB $op.Addr ([BitConverter]::GetBytes([int]$dest)); $done++
        }
    }
}
"{0} instructions modifiees" -f $done
if ($skipped) { "ignorees (operande introuvable) : " + ($skipped -join ' ') }
