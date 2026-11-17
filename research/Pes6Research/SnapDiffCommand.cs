namespace Pes6Research;

/// <summary>
/// Compares three raw snapshots of the same memory range (see <c>snap</c>): A and B taken in the same situation a moment apart, C
/// in the other one. A dword that changed between A and C but is the same in A and B is what the change of situation did (a menu
/// screen, a selected team); what moves by itself (timers, animations) differs between A and B and is left out.
/// </summary>
static class SnapDiffCommand
{
    public const string Usage = "snapdiff -A a.bin -B b.bin -C c.bin -Base 0x7C4000 [-Max 300]\n" +
                                "                                                 dwords equal in A and B but different in C (a change of situation)";

    public static int Run(Options o, string processName)
    {
        var a = File.ReadAllBytes(o.String("A") ?? throw new ToolException("-A is required"));
        var b = File.ReadAllBytes(o.String("B") ?? throw new ToolException("-B is required"));
        var c = File.ReadAllBytes(o.String("C") ?? throw new ToolException("-C is required"));
        var origin = o.Address("Base");
        var max = o.Int("Max", 300);
        var skip = o.String("Skip") is { } k ? k.Split(':').Select(v => (uint)Options.ParseInt(v, "skip")).ToArray() : null;   // a range left out (the player array moves all the time)
        o.CheckAllUsed();
        if (a.Length != b.Length || a.Length != c.Length) throw new ToolException("the three snapshots must have the same length");

        var shown = 0;
        var total = 0;
        var noise = 0;
        for (var i = 0; i + 4 <= a.Length; i += 4)
        {
            var va = BitConverter.ToUInt32(a, i);
            var vb = BitConverter.ToUInt32(b, i);
            var vc = BitConverter.ToUInt32(c, i);
            if (skip != null && origin + (uint)i >= skip[0] && origin + (uint)i < skip[1]) continue;
            if (va != vb) { noise++; continue; }
            if (va == vc) continue;
            total++;
            if (shown++ < max) Console.WriteLine($"0x{origin + (uint)i:X8}: {va,10} (0x{va:X8}) -> {vc,10} (0x{vc:X8})");
        }
        Console.WriteLine($"{total} dwords changed by the change of situation ({noise} moved by themselves between A and B){(total > max ? $"; the first {max} shown" : "")}");
        return 0;
    }
}
