namespace Pes6Research;

/// <summary>
/// Photographs a range of the game's memory at the moment a watched byte takes a given value (the scene counter that says "a stop
/// of play begins"): the photo at that moment (C) and the two taken just before by a rolling snapshot every 100 ms (A the older, B
/// the newer), so that <c>snapdiff</c> keeps what the event changed and leaves what moves by itself. One set of three files per event.
/// </summary>
static class SnapWatchCommand
{
    public const string Usage = "snapwatch -Watch 0x10104C1 -Values 0C,0E [-Address 0x3BC0000] [-Length 0x30000] [-Seconds 600] [-Out folder]\n" +
                                "                                                 three snapshots (A, B before, C at) every time the watched byte takes one of the values";

    public static int Run(Options o, string processName)
    {
        var watch = o.Address("Watch");
        var values = (o.String("Values") ?? throw new ToolException("-Values is required (hex bytes, comma-separated)"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(v => Convert.ToByte(v, 16)).ToHashSet();
        var address = o.Address("Address", 0x3BC0000);
        var length = o.Int("Length", 0x30000);
        var seconds = o.Int("Seconds", 600);
        var folder = Path.GetFullPath(o.String("Out") ?? "snapwatch");
        o.CheckAllUsed();
        Directory.CreateDirectory(folder);
        using var game = GameProcess.Open(processName);

        byte[] Snap()
        {
            var bytes = new byte[length];
            for (var at = 0; at < length; at += 0x1000)
                game.ReadOrZero((uint)(address + at), Math.Min(0x1000, length - at)).CopyTo(bytes, at);
            return bytes;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        byte[] older = Snap(), newer = older;
        long lastRoll = 0;
        var previous = game.ReadOrZero(watch, 1)[0];
        var events = 0;
        using var log = new StreamWriter(Path.Combine(folder, "events.txt"));
        while (clock.Elapsed.TotalSeconds < seconds && !game.HasExited)
        {
            var now = game.ReadOrZero(watch, 1)[0];
            if (now != previous && values.Contains(now))
            {
                var at = Snap();                                   // taken at once: the change is a few ms old
                var id = ++events;
                File.WriteAllBytes(Path.Combine(folder, $"e{id:000}-A.bin"), older);
                File.WriteAllBytes(Path.Combine(folder, $"e{id:000}-B.bin"), newer);
                File.WriteAllBytes(Path.Combine(folder, $"e{id:000}-C.bin"), at);
                var restart = game.ReadOrZero(GameLayout.RestartState, 1)[0];
                log.WriteLine($"e{id:000} t={clock.ElapsedMilliseconds}ms {now:X2} (was {previous:X2}) restart={restart}");
                log.Flush();
                older = newer = Snap();
                lastRoll = clock.ElapsedMilliseconds;
            }
            else if (clock.ElapsedMilliseconds - lastRoll >= 100)
            {
                older = newer;
                newer = Snap();
                lastRoll = clock.ElapsedMilliseconds;
            }
            previous = now;
            Thread.Sleep(5);
        }
        Console.WriteLine($"{events} events in {clock.Elapsed.TotalSeconds:F0} s -> {folder} (range 0x{address:X}, length 0x{length:X})");
        return 0;
    }
}
