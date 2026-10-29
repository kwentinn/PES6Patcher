# Count how many times up to 4 code addresses execute in a running PES6, using hardware
# execute breakpoints for a few seconds (also records the caller of the first address).
# Usage: pes6hits.ps1 -Addresses 0x404338,0x4043A3 -Seconds 3
param([int[]]$Addresses, [int]$Seconds = 3)

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class HitCount {
    [DllImport("kernel32.dll")] static extern bool DebugActiveProcess(int pid);
    [DllImport("kernel32.dll")] static extern bool DebugActiveProcessStop(int pid);
    [DllImport("kernel32.dll")] static extern bool DebugSetProcessKillOnExit(bool kill);
    [DllImport("kernel32.dll")] static extern bool WaitForDebugEvent(byte[] ev, int ms);
    [DllImport("kernel32.dll")] static extern bool ContinueDebugEvent(int pid, int tid, uint status);
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(int access, bool inherit, int tid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int s, out int r);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool Wow64GetThreadContext(IntPtr h, byte[] ctx);
    [DllImport("kernel32.dll")] static extern bool Wow64SetThreadContext(IntPtr h, byte[] ctx);
    [DllImport("kernel32.dll")] static extern uint Wow64SuspendThread(IntPtr h);
    [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr h);

    const int CTX_SIZE = 716, OFF_DR0 = 4, OFF_DR6 = 20, OFF_DR7 = 24, OFF_EIP = 184, OFF_EFL = 192, OFF_ESP = 196, OFF_EDI = 156;
    public static Dictionary<uint, int> Edi = new Dictionary<uint, int>();
    public static Dictionary<uint, int> Arg1 = new Dictionary<uint, int>();
    const uint CTX_FLAGS = 0x10000 | 0x10 | 0x1;

    static void SetDr(int tid, uint[] addrs, bool enable) {
        IntPtr h = OpenThread(0x1FFFFF, false, tid);
        if (h == IntPtr.Zero) return;
        Wow64SuspendThread(h);
        byte[] c = new byte[CTX_SIZE];
        BitConverter.GetBytes(CTX_FLAGS).CopyTo(c, 0);
        if (Wow64GetThreadContext(h, c)) {
            uint dr7 = 0;
            for (int i = 0; i < 4; i++) {
                uint a = (enable && i < addrs.Length) ? addrs[i] : 0u;
                BitConverter.GetBytes(a).CopyTo(c, OFF_DR0 + 4 * i);
                if (enable && i < addrs.Length) dr7 |= 1u << (2 * i);   // Li, execute
            }
            BitConverter.GetBytes(dr7).CopyTo(c, OFF_DR7);
            BitConverter.GetBytes(0u).CopyTo(c, OFF_DR6);
            Wow64SetThreadContext(h, c);
        }
        ResumeThread(h);
        CloseHandle(h);
    }

    public static List<string> Run(int pid, uint[] addrs, int seconds) {
        var counts = new int[addrs.Length];
        var callers = new Dictionary<uint, int>();
        var threads = new HashSet<int>();
        IntPtr proc = OpenProcess(0x0410, false, pid);
        if (!DebugActiveProcess(pid)) throw new Exception("attach impossible");
        DebugSetProcessKillOnExit(false);
        byte[] ev = new byte[176];
        DateTime end = DateTime.Now.AddSeconds(seconds);
        try {
            while (DateTime.Now < end) {
                if (!WaitForDebugEvent(ev, 100)) continue;
                int code = BitConverter.ToInt32(ev, 0), evPid = BitConverter.ToInt32(ev, 4), tid = BitConverter.ToInt32(ev, 8);
                uint status = 0x10002;
                if (threads.Add(tid)) SetDr(tid, addrs, true);
                if (code == 1) {
                    uint exc = BitConverter.ToUInt32(ev, 16);
                    if (exc == 0x80000004 || exc == 0x4000001E) {
                        IntPtr h = OpenThread(0x1FFFFF, false, tid);
                        byte[] c = new byte[CTX_SIZE];
                        BitConverter.GetBytes(CTX_FLAGS).CopyTo(c, 0);
                        Wow64GetThreadContext(h, c);
                        uint eip = BitConverter.ToUInt32(c, OFF_EIP);
                        for (int i = 0; i < addrs.Length; i++) if (addrs[i] == eip) {
                            counts[i]++;
                            if (i == 0) {
                                byte[] b = new byte[4]; int r;
                                ReadProcessMemory(proc, (IntPtr)BitConverter.ToUInt32(c, OFF_ESP), b, 4, out r);
                                uint ret = BitConverter.ToUInt32(b, 0); int n; callers.TryGetValue(ret, out n); callers[ret] = n + 1;
                                uint edi = BitConverter.ToUInt32(c, OFF_EDI); int m; Edi.TryGetValue(edi, out m); Edi[edi] = m + 1;
                                ReadProcessMemory(proc, (IntPtr)(BitConverter.ToUInt32(c, OFF_ESP) + 4), b, 4, out r);
                                uint arg = BitConverter.ToUInt32(b, 0); int k; Arg1.TryGetValue(arg, out k); Arg1[arg] = k + 1;
                            }
                        }
                        BitConverter.GetBytes(BitConverter.ToUInt32(c, OFF_EFL) | 0x10000).CopyTo(c, OFF_EFL);
                        BitConverter.GetBytes(0u).CopyTo(c, OFF_DR6);
                        Wow64SetThreadContext(h, c);
                        CloseHandle(h);
                    } else if (exc != 0x80000003 && exc != 0x4000001F) {
                        status = 0x80010001;
                    }
                }
                ContinueDebugEvent(evPid, tid, status);
            }
        } finally {
            foreach (int t in threads) SetDr(t, addrs, false);
            DebugActiveProcessStop(pid);
            CloseHandle(proc);
        }
        var res = new List<string>();
        for (int i = 0; i < addrs.Length; i++) res.Add(string.Format("0x{0:X}: {1} fois", addrs[i], counts[i]));
        foreach (var kv in callers) res.Add(string.Format("  appelant 0x{0:X}: {1} fois", kv.Key, kv.Value));
        foreach (var kv in Edi) res.Add(string.Format("  EDI 0x{0:X}: {1} fois", kv.Key, kv.Value));
        foreach (var kv in Arg1) res.Add(string.Format("  arg1 0x{0:X}: {1} fois", kv.Key, kv.Value));
        return res;
    }
}
'@

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
[HitCount]::Run($p.Id, [uint32[]]$Addresses, $Seconds)
