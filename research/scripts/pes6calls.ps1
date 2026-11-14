# NOTE: this attaches the debugger. A 12 s session on 0x4C9DC0 was followed 2 minutes later by a crash of the game in the
# graphics driver (2026-11-13), but the game also crashed alone in the menus the next day (same atidxx32.dll fault, only memory reads
# running), so the driver is the likely cause. A trace cave in the exe padding (read with ReadProcessMemory) remains the lighter option.
# Usage: pes6calls.ps1 -Address 0x4C9DC0 -Seconds 12 -Out calls.csv [-NArgs 3]
# Columns: ms, return address, player id, team, slot, state, carrier, ball x, then each argument (int, float), then the return addresses found higher on the stack.
# Execute breakpoint on a function taking (player, x, z): logs every call with its caller, the player
# and the two arguments, plus the ball's x and the carrier. One CSV line per call.
param([int]$Address = 0x4C9DC0, [int]$Seconds = 10, [string]$Out, [int]$NArgs = 3)
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Tgt {
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
    const int CTX_SIZE = 716, OFF_DR0 = 4, OFF_DR6 = 20, OFF_DR7 = 24, OFF_EIP = 184, OFF_EFL = 192, OFF_ESP = 196;
    const uint CTX_FLAGS = 0x10000 | 0x10 | 0x1;
    static void SetDr(int tid, uint addr, bool enable) {
        IntPtr h = OpenThread(0x1FFFFF, false, tid);
        if (h == IntPtr.Zero) return;
        Wow64SuspendThread(h);
        byte[] c = new byte[CTX_SIZE];
        BitConverter.GetBytes(CTX_FLAGS).CopyTo(c, 0);
        if (Wow64GetThreadContext(h, c)) {
            BitConverter.GetBytes(enable ? addr : 0u).CopyTo(c, OFF_DR0);
            BitConverter.GetBytes(enable ? 1u : 0u).CopyTo(c, OFF_DR7);
            BitConverter.GetBytes(0u).CopyTo(c, OFF_DR6);
            Wow64SetThreadContext(h, c);
        }
        ResumeThread(h);
        CloseHandle(h);
    }
    public static List<string> Run(int pid, uint addr, int seconds, int nargs) {
        var rows = new List<string>();
        var threads = new HashSet<int>();
        IntPtr proc = OpenProcess(0x0410, false, pid);
        if (!DebugActiveProcess(pid)) throw new Exception("cannot attach to the game");
        DebugSetProcessKillOnExit(false);
        byte[] ev = new byte[176], st = new byte[64], pl = new byte[0x20], b4 = new byte[4], bl = new byte[4];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try {
            while (sw.Elapsed.TotalSeconds < seconds) {
                if (!WaitForDebugEvent(ev, 100)) continue;
                int code = BitConverter.ToInt32(ev, 0), evPid = BitConverter.ToInt32(ev, 4), tid = BitConverter.ToInt32(ev, 8);
                uint status = 0x10002;
                if (threads.Add(tid)) SetDr(tid, addr, true);
                if (code == 1) {
                    uint exc = BitConverter.ToUInt32(ev, 16);
                    if (exc == 0x80000004 || exc == 0x4000001E) {
                        IntPtr h = OpenThread(0x1FFFFF, false, tid);
                        byte[] c = new byte[CTX_SIZE];
                        BitConverter.GetBytes(CTX_FLAGS).CopyTo(c, 0);
                        Wow64GetThreadContext(h, c);
                        if (BitConverter.ToUInt32(c, OFF_EIP) == addr && rows.Count < 400000) {
                            int r;
                            ReadProcessMemory(proc, (IntPtr)BitConverter.ToUInt32(c, OFF_ESP), st, 64, out r);
                            uint ret = BitConverter.ToUInt32(st, 0), p = BitConverter.ToUInt32(st, 4);
                            ReadProcessMemory(proc, (IntPtr)p, pl, 0x20, out r);
                            ReadProcessMemory(proc, (IntPtr)0x3BE09CC, b4, 4, out r);
                            ReadProcessMemory(proc, (IntPtr)0xBCCE94, bl, 4, out r);
                            ReadProcessMemory(proc, (IntPtr)(BitConverter.ToUInt32(bl, 0) + 0x10), bl, 4, out r);
                            var sb = new System.Text.StringBuilder();
                            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0},{1:X},{2},{3},{4},{5},{6},{7:F0}", sw.ElapsedMilliseconds, ret,
                                ((long)p - 0x3BDC980) / 0x240, pl[0x12], pl[0x11], pl[0x16], b4[0], BitConverter.ToSingle(bl, 0));
                            for (int i = 0; i < nargs - 1; i++) {
                                int v = BitConverter.ToInt32(st, 8 + 4 * i); float f = BitConverter.ToSingle(st, 8 + 4 * i);
                                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, ",{0},{1:F0}", v, (Math.Abs(f) > 0.01 && Math.Abs(f) < 1e6) ? f : 0);
                            }
                            // a few return-looking values further up the stack: the callers' callers
                            for (int i = 4; i < 16; i++) { uint v = BitConverter.ToUInt32(st, 4 * i); if (v > 0x401000 && v < 0x8C0000) sb.AppendFormat(",{0:X}", v); }
                            rows.Add(sb.ToString());
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
            foreach (int t in threads) SetDr(t, addr, false);
            DebugActiveProcessStop(pid);
            CloseHandle(proc);
        }
        return rows;
    }
}
'@
$p = Get-Process PES6 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "PES6 is not running."; exit 1 }
$rows = [Tgt]::Run($p.Id, [uint32]$Address, $Seconds, $NArgs)
$rows | Set-Content $Out
"{0} calls -> {1}" -f $rows.Count, $Out
