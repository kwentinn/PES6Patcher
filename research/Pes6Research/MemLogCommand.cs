namespace Pes6Research;

/// <summary>
/// Logs the changes of a few memory ranges of the running game: a line with the time and the bytes of every range each time any of
/// them differs from the line before (polled about every 15 ms). To catch what a situation (a restart, a menu) changes, with the exact
/// moment, where <c>snap</c> / <c>snapdiff</c> only compare moments chosen by hand.
/// </summary>
static class MemLogCommand
{
    public const string Usage = "memlog -Spec 0x10104C0:16,0x3BE0AA4:1 [-Seconds 60] [-Out memlog.txt]\n" +
                                "                                                 a line with the bytes of the ranges at every change (address:length, hex)";

    public static int Run(Options o, string processName)
    {
        var spec = (o.String("Spec") ?? throw new ToolException("-Spec is required (address:length,...)"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Split(':'))
            .Select(p => (Address: (uint)Options.ParseInt(p[0], "address"), Length: p.Length > 1 ? Options.ParseInt(p[1], "length") : 4))
            .ToList();
        var seconds = o.Int("Seconds", 60);
        var outPath = Path.GetFullPath(o.String("Out") ?? "memlog.txt");
        o.CheckAllUsed();
        using var game = GameProcess.Open(processName);
        using var w = new StreamWriter(outPath);
        w.WriteLine("# " + string.Join(" ", spec.Select(s => $"0x{s.Address:X}:{s.Length}")));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string? last = null;
        var lines = 0;
        while (clock.Elapsed.TotalSeconds < seconds && !game.HasExited)
        {
            var line = string.Join(" | ", spec.Select(s => Convert.ToHexString(game.ReadOrZero(s.Address, s.Length))));
            if (line != last)
            {
                w.WriteLine($"{clock.ElapsedMilliseconds} {line}");
                last = line;
                if (++lines % 20 == 0) w.Flush();
            }
            Thread.Sleep(15);
        }
        Console.WriteLine($"{lines} changes in {clock.Elapsed.TotalSeconds:F0} s -> {outPath}");
        return 0;
    }
}
