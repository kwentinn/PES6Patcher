namespace Pes6Research;

/// <summary>
/// Guided stick capture (was pes6stick.ps1): snapshots the game image's data (0xBC4000-0x5113000) with the stick neutral / held
/// right / neutral / held up, signalled by beeps (1, 2, 1, 3 beeps, then a long low one), then lists the 16-bit locations that
/// follow the stick: X (changes only for right), Y (only for up), XY (both). It led to the controller state at 0x1016680.
/// </summary>
static class StickCommand
{
    public const string Usage = "stick [-StartDelay 5] [-Hold 3] [-Out stick.txt]\n" +
                                "                                                 memory that follows the stick (beeps: 1 neutral, 2 right, 1 neutral, 3 up)";

    const uint Base = 0x00BC4000, End = 0x05113000, Page = 0x1000;

    public static int Run(Options o, string processName)
    {
        var startDelay = o.Int("StartDelay", 5);
        var hold = o.Int("Hold", 3);
        var outPath = o.String("Out");
        o.CheckAllUsed();

        using var game = GameProcess.Open(processName);
        Thread.Sleep(startDelay * 1000);
        Signal(1); Thread.Sleep(hold * 1000); var n1 = Snap(game);   // 1 beep: stick neutral
        Signal(2); Thread.Sleep(hold * 1000); var r = Snap(game);    // 2 beeps: hold right
        Signal(1); Thread.Sleep(hold * 1000); var n2 = Snap(game);   // 1 beep: neutral
        Signal(3); Thread.Sleep(hold * 1000); var u = Snap(game);    // 3 beeps: hold up
        Sampling.Beep(600, 600);                                     // long low beep: done

        var res = new List<string>();
        for (var o2 = 0; o2 + 4 <= n1.Length; o2 += 2)
        {
            int a = BitConverter.ToInt16(n1, o2), b = BitConverter.ToInt16(r, o2), c = BitConverter.ToInt16(n2, o2), d = BitConverter.ToInt16(u, o2);
            if (a != c) continue; // unstable while neutral: noise
            bool xs = b != a && d == a, ys = d != a && b == a, both = b != a && d != a;
            if (!(xs || ys || both)) continue;
            res.Add($"0x{Base + o2:X8}\t{(xs ? "X" : ys ? "Y" : "XY")}\tneutre={a}\tdroite={b}\thaut={d}\t" +
                    $"(i32 {BitConverter.ToInt32(n1, o2 & ~3)} / {BitConverter.ToInt32(r, o2 & ~3)} / {BitConverter.ToInt32(u, o2 & ~3)})");
        }
        Console.WriteLine($"{res.Count} emplacements suivent le stick");
        if (outPath != null) File.WriteAllLines(outPath, res);
        else foreach (var line in res.Take(80)) Console.WriteLine(line);
        return 0;
    }

    /// <summary>The range page by page; unreadable pages stay zero.</summary>
    static byte[] Snap(GameProcess game)
    {
        var all = new byte[End - Base];
        var page = new byte[Page];
        for (var a = Base; a < End; a += Page)
            if (game.TryRead(a, page)) Buffer.BlockCopy(page, 0, all, (int)(a - Base), (int)Page);
        return all;
    }

    static void Signal(int count)
    {
        for (var i = 0; i < count; i++) { Sampling.Beep(1200, 180); Thread.Sleep(120); }
    }
}
