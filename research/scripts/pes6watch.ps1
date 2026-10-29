# Find which instructions write to an address of a running (32-bit) PES6 process, using a
# hardware write breakpoint for a few seconds, then detach without killing the game.
# -Read also traps reads (4-byte breakpoint), to find which code uses a value.
# Usage: pes6watch.ps1 -Address 0x3BECC84 -Seconds 4 [-Read]
param([int]$Address = 0x3BECC84, [int]$Seconds = 4, [switch]$Read)

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class HwWatch {
    [DllImport("kernel32.dll")] static extern bool DebugActiveProcess(int pid);
    [DllImport("kernel32.dll")] static extern bool DebugActiveProcessStop(int pid);
    [DllImport("kernel32.dll")] static extern bool DebugSetProcessKillOnExit(bool kill);
    [DllImport("kernel32.dll")] static extern bool WaitForDebugEvent(byte[] ev, int ms);
    [DllImport("kernel32.dll")] static extern bool ContinueDebugEvent(int pid, int tid, uint status);
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(int access, bool inherit, int tid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool Wow64GetThreadContext(IntPtr h, byte[] ctx);
    [DllImport("kernel32.dll")] static extern bool Wow64SetThreadContext(IntPtr h, byte[] ctx);
    [DllImport("kernel32.dll")] static extern uint Wow64SuspendThread(IntPtr h);
    [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr h);

    const int CTX_SIZE = 716, OFF_DR0 = 4, OFF_DR6 = 20, OFF_DR7 = 24, OFF_EIP = 184;
    const uint CTX_FLAGS = 0x10000 | 0x10 | 0x1;   // i386 | DEBUG_REGISTERS | CONTROL
    const uint DBG_CONTINUE = 0x10002, DBG_NOT_HANDLED = 0x80010001;

    public static uint Dr7 = 0x00050001u;   // L0, RW0 = 01 (write), LEN0 = 01 (2 bytes)

    static bool SetDr(int tid, uint addr, bool enable) {
        IntPtr h = OpenThread(0x1FFFFF, false, tid);
        if (h == IntPtr.Zero) return false;
        Wow64SuspendThread(h);
        byte[] c = new byte[CTX_SIZE];
        BitConverter.GetBytes(CTX_FLAGS).CopyTo(c, 0);
        bool ok = Wow64GetThreadContext(h, c);
        if (ok) {
            BitConverter.GetBytes(enable ? addr : 0u).CopyTo(c, OFF_DR0);
            BitConverter.GetBytes(enable ? Dr7 : 0u).CopyTo(c, OFF_DR7);
            BitConverter.GetBytes(0u).CopyTo(c, OFF_DR6);
            ok = Wow64SetThreadContext(h, c);
        }
        ResumeThread(h);
        CloseHandle(h);
        return ok;
    }

    static uint GetEip(int tid) {
        IntPtr h = OpenThread(0x1FFFFF, false, tid);
        byte[] c = new byte[CTX_SIZE];
        BitConverter.GetBytes(CTX_FLAGS).CopyTo(c, 0);
        Wow64GetThreadContext(h, c);
        CloseHandle(h);
        return BitConverter.ToUInt32(c, OFF_EIP);
    }

    public static Dictionary<uint, int> Run(int pid, uint addr, int seconds) {
        var hits = new Dictionary<uint, int>();
        var threads = new HashSet<int>();
        if (!DebugActiveProcess(pid)) throw new Exception("attach impossible (" + Marshal.GetLastWin32Error() + ")");
        DebugSetProcessKillOnExit(false);
        byte[] ev = new byte[176];
        DateTime end = DateTime.Now.AddSeconds(seconds);
        try {
            while (DateTime.Now < end) {
                if (!WaitForDebugEvent(ev, 100)) continue;
                int code = BitConverter.ToInt32(ev, 0), evPid = BitConverter.ToInt32(ev, 4), tid = BitConverter.ToInt32(ev, 8);
                uint status = DBG_CONTINUE;
                if (threads.Add(tid)) SetDr(tid, addr, true);
                if (code == 1) {   // EXCEPTION_DEBUG_EVENT
                    uint exc = BitConverter.ToUInt32(ev, 16);
                    if (exc == 0x80000004 || exc == 0x4000001E) {   // single step (hardware breakpoint)
                        uint eip = GetEip(tid);
                        int n; hits.TryGetValue(eip, out n); hits[eip] = n + 1;
                    } else if (exc != 0x80000003 && exc != 0x4000001F) {
                        status = DBG_NOT_HANDLED;   // not ours: let the game handle it
                    }
                }
                ContinueDebugEvent(evPid, tid, status);
            }
        } finally {
            foreach (int t in threads) SetDr(t, 0, false);
            DebugActiveProcessStop(pid);
        }
        return hits;
    }
}
'@

$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 n'est pas lance."; exit 1 }
if ($Read) { [HwWatch]::Dr7 = 0x000F0001 }   # RW0 = 11 (read/write), LEN0 = 11 (4 bytes)
$hits = [HwWatch]::Run($p.Id, [uint32]$Address, $Seconds)
if ($hits.Count -eq 0) { "aucune ecriture detectee" }
foreach ($k in $hits.Keys) { "ecriture juste avant EIP 0x{0:X} ({1} fois)" -f $k, $hits[$k] }
