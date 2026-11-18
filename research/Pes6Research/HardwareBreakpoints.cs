using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pes6Research;

/// <summary>
/// Hardware breakpoints in the running (32-bit) game: attaches as a debugger, puts the addresses in DR0-DR3 of every thread (those
/// there and those created later), calls back on each hit, then clears the registers and detaches without killing the game, also
/// on Ctrl+C or an error. The commands calls, hits, watch and viewport use it (they were four copies of it).
/// <para>Attaching the debugger is heavier than reading memory: the game crashed once 2 minutes after a 12 s session, though it also
/// crashes alone in the AMD graphics driver, so the driver is the likely cause. Keep the sessions short.</para>
/// </summary>
sealed class HardwareBreakpoints
{
    /// <summary>One hit: the thread's 32-bit context (registers), and the game's memory to read the stack.</summary>
    public sealed class Hit(byte[] context, GameProcess game)
    {
        public uint Eip => BitConverter.ToUInt32(context, OffEip);
        public uint Esp => BitConverter.ToUInt32(context, OffEsp);
        public uint Edi => BitConverter.ToUInt32(context, OffEdi);
        public uint Ecx => BitConverter.ToUInt32(context, OffEcx);
        public uint Eax => BitConverter.ToUInt32(context, OffEax);
        public GameProcess Game => game;

        /// <summary>The 32-bit value at esp + offset (0: the return address of a call, 4: the first argument).</summary>
        public uint Stack(int offset) => BitConverter.ToUInt32(game.ReadOrZero(Esp + (uint)offset, 4));
    }

    /// <summary>DR7 for execute breakpoints on the first <paramref name="count"/> registers (local enable, RW = 00, LEN = 00).</summary>
    public static uint ExecuteDr7(int count) => Enumerable.Range(0, count).Aggregate(0u, (dr7, i) => dr7 | 1u << (2 * i));

    /// <summary>DR7 for a data breakpoint on DR0: write (RW0 = 01) of 2 bytes, or read/write (RW0 = 11) of 4 bytes; one byte (LEN0 = 00) if asked.</summary>
    public static uint DataDr7(bool read, bool oneByte = false) => oneByte ? (read ? 0x00030001u : 0x00010001u) : read ? 0x000F0001u : 0x00050001u;

    /// <summary>
    /// Runs for <paramref name="seconds"/> with the addresses in DR0.. and DR7 as given. For execute breakpoints the resume flag is
    /// set after each hit, so the instruction runs instead of trapping again.
    /// </summary>
    public static void Run(GameProcess game, int processId, uint[] addresses, uint dr7, bool execute, double seconds, Action<Hit> onHit)
    {
        if (addresses.Length is 0 or > 4) throw new ToolException("1 to 4 addresses");
        var threads = new HashSet<int>();
        if (!DebugActiveProcess(processId)) throw new ToolException($"cannot attach to the game (error {Marshal.GetLastWin32Error()})");
        DebugSetProcessKillOnExit(false);
        var stop = false;
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop = true; };
        Console.CancelKeyPress += cancel;
        var ev = new byte[176];
        var clock = Stopwatch.StartNew();
        try
        {
            while (!stop && clock.Elapsed.TotalSeconds < seconds)
            {
                if (!WaitForDebugEvent(ev, 100)) continue;
                int code = BitConverter.ToInt32(ev, 0), pid = BitConverter.ToInt32(ev, 4), tid = BitConverter.ToInt32(ev, 8);
                var status = DbgContinue;
                if (threads.Add(tid)) SetDebugRegisters(tid, addresses, dr7);
                if (code == ExitProcessEvent) { stop = true; Console.Error.WriteLine("the game closed"); }
                if (code == ExceptionEvent)
                {
                    var exception = BitConverter.ToUInt32(ev, 16);
                    if (exception is SingleStep or Wow64SingleStep) OnSingleStep(game, tid, execute, onHit);
                    else if (exception is not (Breakpoint or Wow64Breakpoint)) status = DbgExceptionNotHandled; // not ours: the game handles it
                }
                ContinueDebugEvent(pid, tid, status);
            }
        }
        finally
        {
            foreach (var t in threads) SetDebugRegisters(t, [], 0);
            Drain(ev);
            DebugActiveProcessStop(processId);
            Console.CancelKeyPress -= cancel;
        }
    }

    /// <summary>
    /// Before detaching: a thread may have hit a breakpoint just before the registers were cleared, and its single-step event may
    /// still be queued. Delivered to the game after the detach, nobody would handle it and the game would crash (it did, on a
    /// function called about 200 times a second). So the queued events are taken and continued here until none comes.
    /// </summary>
    static void Drain(byte[] ev)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 2000 && WaitForDebugEvent(ev, 50))
        {
            int code = BitConverter.ToInt32(ev, 0), pid = BitConverter.ToInt32(ev, 4), tid = BitConverter.ToInt32(ev, 8);
            var status = DbgContinue;
            if (code == ExceptionEvent)
            {
                var exception = BitConverter.ToUInt32(ev, 16);
                if (exception is SingleStep or Wow64SingleStep) SetDebugRegisters(tid, [], 0);   // ours: clear DR6 too, then go on
                else if (exception is not (Breakpoint or Wow64Breakpoint)) status = DbgExceptionNotHandled;
            }
            ContinueDebugEvent(pid, tid, status);
        }
    }

    static void OnSingleStep(GameProcess game, int tid, bool execute, Action<Hit> onHit)
    {
        var h = OpenThread(ThreadAllAccess, false, tid);
        if (h == IntPtr.Zero) return;
        try
        {
            var c = NewContext();
            if (!Wow64GetThreadContext(h, c)) return;
            onHit(new Hit(c, game));
            if (!execute) return;
            BitConverter.GetBytes(BitConverter.ToUInt32(c, OffEfl) | ResumeFlag).CopyTo(c, OffEfl);
            BitConverter.GetBytes(0u).CopyTo(c, OffDr6);
            Wow64SetThreadContext(h, c);
        }
        finally { CloseHandle(h); }
    }

    static void SetDebugRegisters(int tid, uint[] addresses, uint dr7)
    {
        var h = OpenThread(ThreadAllAccess, false, tid);
        if (h == IntPtr.Zero) return;
        Wow64SuspendThread(h);
        var c = NewContext();
        if (Wow64GetThreadContext(h, c))
        {
            for (var i = 0; i < 4; i++) BitConverter.GetBytes(i < addresses.Length ? addresses[i] : 0u).CopyTo(c, OffDr0 + 4 * i);
            BitConverter.GetBytes(dr7).CopyTo(c, OffDr7);
            BitConverter.GetBytes(0u).CopyTo(c, OffDr6);
            Wow64SetThreadContext(h, c);
        }
        ResumeThread(h);
        CloseHandle(h);
    }

    static byte[] NewContext()
    {
        var c = new byte[ContextSize];
        BitConverter.GetBytes(ContextFlags).CopyTo(c, 0);
        return c;
    }

    // WOW64_CONTEXT
    const int ContextSize = 716, OffDr0 = 4, OffDr6 = 20, OffDr7 = 24, OffEdi = 156, OffEcx = 172, OffEax = 176, OffEip = 184, OffEfl = 192, OffEsp = 196;
    const uint ContextFlags = 0x10000 | 0x10 | 0x1 | 0x2; // i386 | DEBUG_REGISTERS | CONTROL | INTEGER (EAX, ECX, EDI: without it they read as 0)
    const uint ResumeFlag = 0x10000;
    const int ThreadAllAccess = 0x1FFFFF, ExceptionEvent = 1, ExitProcessEvent = 5;
    const uint DbgContinue = 0x10002, DbgExceptionNotHandled = 0x80010001;
    const uint SingleStep = 0x80000004, Wow64SingleStep = 0x4000001E, Breakpoint = 0x80000003, Wow64Breakpoint = 0x4000001F;

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool DebugActiveProcess(int pid);
    [DllImport("kernel32.dll")] static extern bool DebugActiveProcessStop(int pid);
    [DllImport("kernel32.dll")] static extern bool DebugSetProcessKillOnExit(bool kill);
    [DllImport("kernel32.dll")] static extern bool WaitForDebugEvent(byte[] ev, int ms);
    [DllImport("kernel32.dll")] static extern bool ContinueDebugEvent(int pid, int tid, uint status);
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(int access, bool inherit, int tid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool Wow64GetThreadContext(IntPtr h, byte[] context);
    [DllImport("kernel32.dll")] static extern bool Wow64SetThreadContext(IntPtr h, byte[] context);
    [DllImport("kernel32.dll")] static extern uint Wow64SuspendThread(IntPtr h);
    [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr h);
}
