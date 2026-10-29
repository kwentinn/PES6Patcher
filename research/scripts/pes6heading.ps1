# Beep-guided measurement: samples the stick angle (pad 0) and the heading (+0x2C) of the
# given player objects every 100 ms, then summarises how many distinct headings each player
# took and whether they are 45-degree multiples.
# Usage: pes6heading.ps1 -Players 0x3BDD700,0x3BDDB80 [-Seconds 10]
param([int[]]$Players, [int]$Seconds = 10, [int]$StartDelay = 5)

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HeadMem {
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a, bool i, int p);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int s, out int r);
    [DllImport("kernel32.dll")] public static extern bool Beep(int f, int ms);
}
'@

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
$h = [HeadMem]::OpenProcess(0x10, $false, $p.Id)
function U16([int]$a) { $b = New-Object byte[] 2; $n = 0; [void][HeadMem]::ReadProcessMemory($h, [IntPtr]$a, $b, 2, [ref]$n); [BitConverter]::ToUInt16($b, 0) }
function Deg([int]$v) { [math]::Round($v * 360.0 / 65536, 1) }

Start-Sleep -Seconds $StartDelay
[void][HeadMem]::Beep(1200, 300)
$samples = @()
$end = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $end) {
    $row = [ordered]@{ stick = (U16 0x1016680); mag = ((U16 0x1016682) -band 0xFF) }
    foreach ($pl in $Players) { $row[('0x{0:X}' -f $pl)] = U16 ($pl + 0x2C) }
    $samples += [pscustomobject]$row
    Start-Sleep -Milliseconds 100
}
[void][HeadMem]::Beep(600, 700)

$moving = $samples | Where-Object { $_.mag -gt 0 }
"{0} relevés, {1} avec le stick poussé" -f $samples.Count, $moving.Count
"stick: " + (($moving | ForEach-Object { Deg $_.stick } | Group-Object | Sort-Object Count -Descending | Select-Object -First 6 | ForEach-Object { "$($_.Name)° x$($_.Count)" }) -join ', ')
foreach ($pl in $Players) {
    $k = '0x{0:X}' -f $pl
    $vals = $moving | ForEach-Object { $_.$k }
    $distinct = $vals | Sort-Object -Unique
    $off45 = @($vals | Where-Object { ($_ % 0x2000) -ne 0 }).Count
    "{0}: {1} caps différents, {2}/{3} hors multiples de 45° ; plus fréquents: {4}" -f $k, @($distinct).Count, $off45, @($vals).Count,
        (($vals | Group-Object | Sort-Object Count -Descending | Select-Object -First 5 | ForEach-Object { "$(Deg $_.Name)° x$($_.Count)" }) -join ', ')
}
