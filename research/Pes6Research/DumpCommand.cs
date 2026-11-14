namespace Pes6Research;

/// <summary>A memory range of the running game as int16 / int32 / float columns, one line per 4 bytes (was pes6dump.ps1).</summary>
static class DumpCommand
{
    public const string Usage = "dump -Address 0x3BECC60 [-Length 0x40]          memory as int16 / int32 / float columns";

    public static int Run(Options o, string processName)
    {
        var address = o.Positional.Count > 0 ? (uint)Options.ParseInt(o.Positional[0], "Address") : o.Address("Address");
        var length = o.Positional.Count > 1 ? Options.ParseInt(o.Positional[1], "Length") : o.Int("Length", 0x40);
        o.CheckAllUsed();
        if (length <= 0) throw new ToolException("-Length must be positive");

        using var game = GameProcess.Open(processName);
        var buf = game.Read(address, (length + 3) & ~3);
        var w = new StringWriter();
        for (var off = 0; off < length; off += 4)
            w.WriteLine($"0x{address + off:X8}: i16={BitConverter.ToInt16(buf, off),6} {BitConverter.ToInt16(buf, off + 2),6}  " +
                        $"i32={BitConverter.ToInt32(buf, off),11}  f={BitConverter.ToSingle(buf, off)}");
        Console.Write(w);
        return 0;
    }
}
