using System.Diagnostics;
using System.Globalization;
using static Pes6Research.GameLayout;

namespace Pes6Research;

/// <summary>
/// The commands that attach the debugger (hardware breakpoints, see <see cref="HardwareBreakpoints"/>): pes6calls.ps1,
/// pes6hits.ps1, pes6watch.ps1 and pes6viewport.ps1. Keep the sessions short.
/// </summary>
static class BreakpointCommands
{
    public const string CallsUsage = "calls -Address 0x4C9DC0 [-Seconds 10] [-Out calls.csv] [-NArgs 3]\n" +
                                     "                                                 debugger: every call of a function (player, args), with its callers";
    public const string HitsUsage = "hits -Addresses 0x404338,0x4043A3 [-Seconds 3]\n" +
                                    "                                                 debugger: how many times up to 4 addresses run; callers, EDI, arg 1 of the first";
    public const string WatchUsage = "watch -Address 0x3BECC84 [-Seconds 4] [-Read]\n" +
                                     "                                                 debugger: the instructions that write (or -Read: use) an address";
    public const string ViewportUsage = "viewport [-Seconds 3]                            debugger: the Direct3D SetViewport calls (caller, rectangle)";

    /// <summary>
    /// Execute breakpoint on a function taking (player, ...): one CSV line per call. Columns: ms, return address, player id, team,
    /// slot, state, carrier, ball x, then each further argument (int, float), then the return addresses found higher on the stack.
    /// </summary>
    public static int Calls(Options o, string processName)
    {
        var address = o.Address("Address", 0x4C9DC0);
        var seconds = o.Int("Seconds", 10);
        var outPath = Path.GetFullPath(o.String("Out") ?? "calls.csv");
        var nArgs = o.Int("NArgs", 3);
        o.CheckAllUsed();

        using var game = GameProcess.Open(processName);
        using var w = new StreamWriter(outPath);
        var rows = 0;
        var clock = Stopwatch.StartNew();
        HardwareBreakpoints.Run(game, game.Id, [address], HardwareBreakpoints.ExecuteDr7(1), execute: true, seconds, hit =>
        {
            if (hit.Eip != address || rows >= 400000) return;
            var st = game.ReadOrZero(hit.Esp, 64);
            uint ret = BitConverter.ToUInt32(st, 0), p = BitConverter.ToUInt32(st, 4);
            var pl = game.ReadOrZero(p, 0x20);
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{clock.ElapsedMilliseconds},{ret:X},{((long)p - 0x3BDC980) / PlayerSize},{pl[Team]},{pl[Slot]},{pl[State]}," +
                $"{game.ReadOrZero(Carrier, 1)[0]},{Ball(game).X:F0}");
            for (var i = 0; i < nArgs - 1; i++)
            {
                int v = BitConverter.ToInt32(st, 8 + 4 * i); var f = BitConverter.ToSingle(st, 8 + 4 * i);
                line += string.Create(CultureInfo.InvariantCulture, $",{v},{(Math.Abs(f) > 0.01 && Math.Abs(f) < 1e6 ? f : 0):F0}");
            }
            // a few return-looking values further up the stack: the callers' callers
            for (var i = 4; i < 16; i++) { var v = BitConverter.ToUInt32(st, 4 * i); if (v is > 0x401000 and < 0x8C0000) line += $",{v:X}"; }
            w.WriteLine(line);
            rows++;
        });
        Console.WriteLine($"{rows} calls -> {outPath}");
        return 0;
    }

    /// <summary>How many times up to 4 code addresses run; for the first, its callers, EDI and first argument.</summary>
    public static int Hits(Options o, string processName)
    {
        var addresses = o.List("Addresses").Select(a => (uint)Options.ParseInt(a, "Addresses")).ToArray();
        var seconds = o.Int("Seconds", 3);
        o.CheckAllUsed();
        if (addresses.Length is 0 or > 4) throw new ToolException("-Addresses takes 1 to 4 addresses");

        using var game = GameProcess.Open(processName);
        var counts = new int[addresses.Length];
        Dictionary<uint, int> callers = [], edi = [], arg1 = [];
        HardwareBreakpoints.Run(game, game.Id, addresses, HardwareBreakpoints.ExecuteDr7(addresses.Length), execute: true, seconds, hit =>
        {
            for (var i = 0; i < addresses.Length; i++)
            {
                if (addresses[i] != hit.Eip) continue;
                counts[i]++;
                if (i != 0) continue;
                Count(callers, hit.Stack(0));
                Count(edi, hit.Edi);
                Count(arg1, hit.Stack(4));
            }
        });
        for (var i = 0; i < addresses.Length; i++) Console.WriteLine($"0x{addresses[i]:X}: {counts[i]} times");
        foreach (var (k, n) in callers) Console.WriteLine($"  caller 0x{k:X}: {n} times");
        foreach (var (k, n) in edi) Console.WriteLine($"  EDI 0x{k:X}: {n} times");
        foreach (var (k, n) in arg1) Console.WriteLine($"  arg1 0x{k:X}: {n} times");
        return 0;
    }

    /// <summary>
    /// The instructions that write an address (data breakpoint on 2 bytes), or with -Read that read or write it (4 bytes). The
    /// processor stops after the instruction, so the address shown is the one just after it.
    /// </summary>
    public static int Watch(Options o, string processName)
    {
        var address = o.Address("Address", 0x3BECC84);
        var seconds = o.Int("Seconds", 4);
        var read = o.Flag("Read");
        o.CheckAllUsed();

        using var game = GameProcess.Open(processName);
        var hits = new Dictionary<uint, int>();
        HardwareBreakpoints.Run(game, game.Id, [address], HardwareBreakpoints.DataDr7(read), execute: false, seconds, hit => Count(hits, hit.Eip));
        if (hits.Count == 0) Console.WriteLine(read ? "no access detected" : "no write detected");
        foreach (var (eip, n) in hits) Console.WriteLine($"{(read ? "access" : "write")} just before EIP 0x{eip:X} ({n} times)");
        return 0;
    }

    /// <summary>The IDirect3DDevice8::SetViewport calls: caller return address and requested rectangle (it led to the list clipping).</summary>
    public static int Viewport(Options o, string processName)
    {
        var seconds = o.Int("Seconds", 3);
        o.CheckAllUsed();

        using var game = GameProcess.Open(processName);
        uint U32(uint a) => BitConverter.ToUInt32(game.ReadOrZero(a, 4));
        var device = U32(0x00F83E68);
        var setViewport = U32(U32(device) + 0xA0); // the device's vtable, method 40
        if (setViewport == 0) throw new ToolException("no Direct3D device yet (start the game past its first screen)");
        var log = new Dictionary<string, int>();
        HardwareBreakpoints.Run(game, game.Id, [setViewport], HardwareBreakpoints.ExecuteDr7(1), execute: true, seconds, hit =>
        {
            var vp = hit.Stack(8);
            var key = $"caller 0x{hit.Stack(0):X}  viewport x={U32(vp)} y={U32(vp + 4)} w={U32(vp + 8)} h={U32(vp + 12)}";
            log[key] = log.GetValueOrDefault(key) + 1;
        });
        Console.WriteLine($"SetViewport = 0x{setViewport:X}");
        foreach (var (k, n) in log) Console.WriteLine($"{k}  ({n} times)");
        return 0;
    }

    static void Count(Dictionary<uint, int> d, uint key) => d[key] = d.GetValueOrDefault(key) + 1;
}
