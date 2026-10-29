# Log IDirect3DDevice8::SetViewport calls of a running PES6 process for a few seconds,
# using a hardware execute breakpoint: caller return address + requested rectangle.
# Usage: pes6viewport.ps1 -Seconds 3
param([int]$Seconds = 3)

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class VpWatch {
    [DllImport("kernel32.dll")] static extern bool DebugActiveProcess(int pid);
    [DllImport("kernel32.dll")] static extern bool DebugActiveProcessStop(int pid);
    [DllImport("kernel32.dll")] static extern bool DebugSetProcessKillOnExit(bool kill);
    [DllImport("kernel32.dll")] static extern bool WaitForDebugEvent(byte[] ev, int ms);
    [DllImport("kernel32.dll")] static extern bool ContinueDebugEvent(int pid, int tid, uint status);
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(int access, bool inherit, int tid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int s, out int r);
    [DllImport("kernel32.dll")] static extern bool Wow64GetThreadContext(IntPtr h, byte[] ctx);
    [DllImport("kernel32.dll")] static extern bool Wow64SetThreadContext(IntPtr h, byte[] ctx);
    [DllImport("kernel32.dll")] static extern uint Wow64SuspendThread(IntPtr h);
    [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr h);

    const int CTX_SIZE = 716, OFF_DR0 = 4, OFF_DR6 = 20, OFF_DR7 = 24, OFF_EFL = 192, OFF_ESP = 196;
    const uint CTX_FLAGS = 0x10000 | 0x10 | 0x1;
    const uint DBG_CONTINUE = 0x10002, DBG_NOT_HANDLED = 0x80010001;

    static IntPtr proc;

    static uint ReadU32(uint a) { byte[] b = new byte[4]; int r; ReadProcessMemory(proc, (IntPtr)a, b, 4, out r); return BitConverter.ToUInt32(b, 0); }

    static void SetDr(int tid, uint addr, bool enable) {
        IntPtr h = OpenThread(0x1FFFFF, false, tid);
        if (h == IntPtr.Zero) return;
        Wow64SuspendThread(h);
        byte[] c = new byte[CTX_SIZE];
        BitConverter.GetBytes(CTX_FLAGS).CopyTo(c, 0);
        if (Wow64GetThreadContext(h, c)) {
            BitConverter.GetBytes(enable ? addr : 0u).CopyTo(c, OFF_DR0);
            BitConverter.GetBytes(enable ? 0x00000001u : 0u).CopyTo(c, OFF_DR7);   // L0, execute
            BitConverter.GetBytes(0u).CopyTo(c, OFF_DR6);
            Wow64SetThreadContext(h, c);
        }
        ResumeThread(h);
        CloseHandle(h);
    }

    public static List<string> Run(int pid, int seconds) {
        var log = new Dictionary<string, int>();
        var threads = new HashSet<int>();
        proc = OpenProcess(0x0410, false, pid);
        uint dev = ReadU32(0x00F83E68);
        uint vtbl = ReadU32(dev);
        uint setViewport = ReadU32(vtbl + 0xA0);
        if (!DebugActiveProcess(pid)) throw new Exception("attach impossible");
        DebugSetProcessKillOnExit(false);
        byte[] ev = new byte[176];
        DateTime end = DateTime.Now.AddSeconds(seconds);
        try {
            while (DateTime.Now < end) {
                if (!WaitForDebugEvent(ev, 100)) continue;
                int code = BitConverter.ToInt32(ev, 0), evPid = BitConverter.ToInt32(ev, 4), tid = BitConverter.ToInt32(ev, 8);
                uint status = DBG_CONTINUE;
                if (threads.Add(tid)) SetDr(tid, setViewport, true);
                if (code == 1) {
                    uint exc = BitConverter.ToUInt32(ev, 16);
                    if (exc == 0x80000004 || exc == 0x4000001E) {
                        IntPtr h = OpenThread(0x1FFFFF, false, tid);
                        byte[] c = new byte[CTX_SIZE];
                        BitConverter.GetBytes(CTX_FLAGS).CopyTo(c, 0);
                        Wow64GetThreadContext(h, c);
                        uint esp = BitConverter.ToUInt32(c, OFF_ESP);
                        uint ret = ReadU32(esp), vp = ReadU32(esp + 8);
                        string key = string.Format("appelant 0x{0:X}  viewport x={1} y={2} w={3} h={4}",
                            ret, ReadU32(vp), ReadU32(vp + 4), ReadU32(vp + 8), ReadU32(vp + 12));
                        int n; log.TryGetValue(key, out n); log[key] = n + 1;
                        // resume flag so the breakpoint does not fire again on the same instruction
                        uint efl = BitConverter.ToUInt32(c, OFF_EFL) | 0x10000;
                        BitConverter.GetBytes(efl).CopyTo(c, OFF_EFL);
                        BitConverter.GetBytes(0u).CopyTo(c, OFF_DR6);
                        Wow64SetThreadContext(h, c);
                        CloseHandle(h);
                    } else if (exc != 0x80000003 && exc != 0x4000001F) {
                        status = DBG_NOT_HANDLED;
                    }
                }
                ContinueDebugEvent(evPid, tid, status);
            }
        } finally {
            foreach (int t in threads) SetDr(t, 0, false);
            DebugActiveProcessStop(pid);
            CloseHandle(proc);
        }
        var result = new List<string>();
        result.Add(string.Format("SetViewport = 0x{0:X}", setViewport));
        foreach (var kv in log) result.Add(kv.Key + "  (" + kv.Value + " fois)");
        return result;
    }
}
'@

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
[VpWatch]::Run($p.Id, $Seconds)
