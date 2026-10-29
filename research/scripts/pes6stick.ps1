# Guided stick capture: snapshots the game image's data (0xBC4000-0x5113000) with the stick
# neutral / held right / neutral / held up, signalled by beeps, then lists the locations that
# follow the stick: X axis (changes only for "right"), Y axis (changes only for "up").
# Usage: pes6stick.ps1 [-StartDelay 5] [-Hold 3]
param([int]$StartDelay = 5, [int]$Hold = 3, [string]$Out = "")

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

public static class StickScan {
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int a, bool i, int p);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, IntPtr s, out IntPtr r);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool Beep(int freq, int ms);

    const long Base = 0x00BC4000, End = 0x05113000, Page = 0x1000;

    static byte[] Snap(IntPtr h) {
        var all = new byte[End - Base];
        var page = new byte[Page];
        for (long a = Base; a < End; a += Page) {
            IntPtr n;
            if (ReadProcessMemory(h, (IntPtr)a, page, (IntPtr)Page, out n)) Buffer.BlockCopy(page, 0, all, (int)(a - Base), (int)Page);
        }
        return all;
    }

    static void Signal(int count) { for (int i = 0; i < count; i++) { Beep(1200, 180); Thread.Sleep(120); } }

    public static List<string> Run(int pid, int startDelay, int hold) {
        IntPtr h = OpenProcess(0x0410, false, pid);
        Thread.Sleep(startDelay * 1000);
        Signal(1); Thread.Sleep(hold * 1000); var n1 = Snap(h);          // 1 beep: stick neutral
        Signal(2); Thread.Sleep(hold * 1000); var r  = Snap(h);          // 2 beeps: hold right
        Signal(1); Thread.Sleep(hold * 1000); var n2 = Snap(h);          // 1 beep: neutral
        Signal(3); Thread.Sleep(hold * 1000); var u  = Snap(h);          // 3 beeps: hold up
        Beep(600, 600);                                                  // long low beep: done
        CloseHandle(h);

        var res = new List<string>();
        for (int o = 0; o + 4 <= n1.Length; o += 2) {
            int a = BitConverter.ToInt16(n1, o), b = BitConverter.ToInt16(r, o), c = BitConverter.ToInt16(n2, o), d = BitConverter.ToInt16(u, o);
            if (a != c) continue;                     // unstable while neutral: noise
            bool xs = b != a && d == a, ys = d != a && b == a, both = b != a && d != a;
            if (!(xs || ys || both)) continue;
            res.Add(string.Format("0x{0:X8}\t{1}\tneutre={2}\tdroite={3}\thaut={4}\t(i32 {5} / {6} / {7})",
                Base + o, xs ? "X" : ys ? "Y" : "XY", a, b, d,
                BitConverter.ToInt32(n1, o & ~3), BitConverter.ToInt32(r, o & ~3), BitConverter.ToInt32(u, o & ~3)));
        }
        return res;
    }
}
'@

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
$r = [StickScan]::Run($p.Id, $StartDelay, $Hold)
"{0} emplacements suivent le stick" -f $r.Count
if ($Out) { $r | Set-Content $Out } else { $r | Select-Object -First 80 }
