# Sample the writable memory of a running PES6 for a while (e.g. while the stick is rotated
# with the game paused) and list 4-byte locations whose value keeps changing:
# many distinct values = raw analog axis candidates, 2..9 distinct values = quantized direction.
# Usage: pes6scan.ps1 -Seconds 20 -IntervalMs 500
param([int]$Seconds = 20, [int]$IntervalMs = 500, [string]$Out = "")

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

public static class MemScan {
    [StructLayout(LayoutKind.Sequential)]
    struct MBI { public ulong BaseAddress, AllocationBase; public uint AllocationProtect, _a; public ulong RegionSize; public uint State, Protect, Type, _b; }
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int a, bool i, int p);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, IntPtr s, out IntPtr r);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualQueryEx(IntPtr h, IntPtr a, out MBI m, IntPtr len);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    public static List<string> Run(int pid, int seconds, int intervalMs) {
        IntPtr h = OpenProcess(0x0410, false, pid);
        // writable committed regions below 2 GB, excluding huge ones (textures)
        var regions = new List<KeyValuePair<long, int>>();
        long addr = 0x10000;
        while (addr < 0x7FFF0000) {
            MBI m;
            if (VirtualQueryEx(h, (IntPtr)addr, out m, (IntPtr)Marshal.SizeOf(typeof(MBI))) == IntPtr.Zero) break;
            bool writable = (m.Protect & 0xCC) != 0 && (m.Protect & 0x100) == 0;   // RW, WC, XRW, XWC, no guard
            if (m.State == 0x1000 && writable && m.RegionSize <= 64UL * 1024 * 1024)
                regions.Add(new KeyValuePair<long, int>((long)m.BaseAddress, (int)m.RegionSize));
            addr = (long)m.BaseAddress + (long)m.RegionSize;
        }
        var first = new Dictionary<long, byte[]>();
        foreach (var r in regions) { var b = new byte[r.Value]; IntPtr n; if (ReadProcessMemory(h, (IntPtr)r.Key, b, (IntPtr)r.Value, out n)) first[r.Key] = b; }
        var seen = new Dictionary<long, HashSet<int>>();
        int samples = Math.Max(1, seconds * 1000 / intervalMs);
        for (int s = 0; s < samples; s++) {
            Thread.Sleep(intervalMs);
            foreach (var kv in first) {
                var cur = new byte[kv.Value.Length]; IntPtr n;
                if (!ReadProcessMemory(h, (IntPtr)kv.Key, cur, (IntPtr)cur.Length, out n)) continue;
                for (int o = 0; o + 4 <= cur.Length; o += 4) {
                    if (cur[o] == kv.Value[o] && cur[o+1] == kv.Value[o+1] && cur[o+2] == kv.Value[o+2] && cur[o+3] == kv.Value[o+3]) continue;
                    long a = kv.Key + o;
                    HashSet<int> set;
                    if (!seen.TryGetValue(a, out set)) { set = new HashSet<int>(); set.Add(BitConverter.ToInt32(kv.Value, o)); seen[a] = set; }
                    if (set.Count < 400) set.Add(BitConverter.ToInt32(cur, o));
                }
            }
        }
        CloseHandle(h);
        var res = new List<string>();
        res.Add(string.Format("regions={0} samples={1} changing={2}", regions.Count, samples, seen.Count));
        foreach (var kv in seen) {
            int n = kv.Value.Count;
            if (n < 2) continue;
            int mn = int.MaxValue, mx = int.MinValue; bool small = true;
            foreach (int v in kv.Value) { mn = Math.Min(mn, v); mx = Math.Max(mx, v); if (v < -70000 || v > 70000) small = false; }
            string vals = "";
            if (n <= 12) { var l = new List<int>(kv.Value); l.Sort(); vals = string.Join(",", l); }
            res.Add(string.Format("0x{0:X8}\tdistinct={1}\tmin={2}\tmax={3}\tsmallint={4}\t{5}", kv.Key, n, mn, mx, small, vals));
        }
        return res;
    }
}
'@

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
$r = [MemScan]::Run($p.Id, $Seconds, $IntervalMs)
if ($Out) { $r | Set-Content $Out; $r[0] } else { $r }
