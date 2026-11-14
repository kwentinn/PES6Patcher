namespace Pes6Research;

/// <summary>Commands on the exe file (the game need not run): pes6findref.ps1 and pes6readva.ps1.</summary>
static class ExeCommands
{
    public const string FindRefUsage = "findref -Exe PES6.exe.orig -Values 0x3BCDCAE,0x3BE0A6E\n" +
                                       "                                                 places of the exe file holding a 32-bit value, with 8 bytes before and 6 after";
    public const string ReadVaUsage = "readva -Exe PES6.exe.orig -Addresses 0xB8C10C,0x5E23FC [-Length 8]\n" +
                                      "                                                 float, int32 and bytes of the exe file at virtual addresses";

    public static int FindRef(Options o, string _)
    {
        var pe = new PeFile(o.String("Exe") ?? throw new ToolException("-Exe is required"));
        var values = o.List("Values").Select(v => (uint)Options.ParseInt(v, "Values")).ToList();
        o.CheckAllUsed();
        if (values.Count == 0) throw new ToolException("-Values is required");

        var b = pe.Bytes;
        var w = new StringWriter();
        foreach (var v in values)
        {
            w.WriteLine($"== 0x{v:X}");
            var pattern = BitConverter.GetBytes(v);
            var span = b.AsSpan();
            // 8 bytes before and 6 after each hit must exist
            for (var from = 8; ;)
            {
                var j = span[from..].IndexOf(pattern);
                if (j < 0) break;
                var at = from + j;
                from = at + 1;
                if (at >= b.Length - 10) break;
                if (pe.SectionAtOffset(at) is not { } s) continue;
                w.WriteLine($"{s.Name,-6} 0x{pe.VirtualAddress(s, at):X}: {CodeCommand.FormatHex(span.Slice(at - 8, 8))} " +
                            $"[{CodeCommand.FormatHex(span.Slice(at, 4))}] {CodeCommand.FormatHex(span.Slice(at + 4, 6))}");
            }
        }
        Console.Write(w);
        return 0;
    }

    public static int ReadVa(Options o, string _)
    {
        var pe = new PeFile(o.String("Exe") ?? throw new ToolException("-Exe is required"));
        var addresses = o.List("Addresses").Select(v => (uint)Options.ParseInt(v, "Addresses")).ToList();
        var length = o.Int("Length", 8);
        o.CheckAllUsed();
        if (addresses.Count == 0) throw new ToolException("-Addresses is required");

        foreach (var a in addresses)
        {
            var f = pe.Offset(a);
            if (f < 0 || f + Math.Max(length, 4) > pe.Bytes.Length) { Console.WriteLine($"0x{a:X}: not in the file"); continue; }
            Console.WriteLine($"0x{a:X}: f={BitConverter.ToSingle(pe.Bytes, (int)f)} i={BitConverter.ToInt32(pe.Bytes, (int)f)} " +
                              $"bytes={CodeCommand.FormatHex(pe.Bytes.AsSpan((int)f, length))}");
        }
        return 0;
    }
}
