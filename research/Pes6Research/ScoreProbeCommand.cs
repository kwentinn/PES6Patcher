using static Pes6Research.GameLayout;

namespace Pes6Research;

/// <summary>
/// Looks for the score counters of a match in memory (was pes6scoreprobe.ps1): samples a region of the game's data every -Every ms
/// and records each 32-bit value that goes up by exactly 1 (to at most 15) and never does anything else, with the ball's position at
/// that moment. Goals make two such counters move (the scores); shots and corners make others, but only a goal happens with the ball
/// in the net. Goal sequences wait for Start: with -SkipScan (1 = Esc), the key is sent when the ball sits in a goal (|x| &gt; 13500,
/// |z| &lt; 1500) and nobody moves for 3 s, at most every 4 s, with the game brought to the front; 0 turns that off. The speed
/// patch is put back at the end.
/// </summary>
static class ScoreProbeCommand
{
    public const string Usage = "scoreprobe [-Seconds 600] [-Every 400] [-Start 0x3B80000] [-Length 0x100000] [-SkipScan 1] [-SpeedPatch speed-x2]\n" +
                                "   [-Out score-candidates.txt]                   32-bit values that only ever go up by 1, with the ball (score counters)";

    public static int Run(Options o, string processName)
    {
        var seconds = o.Int("Seconds", 600);
        var every = o.Int("Every", 400);
        var start = o.Address("Start", 0x3B80000);
        var length = o.Int("Length", 0x100000);
        var skipScan = o.Int("SkipScan", 1);
        var speedPatch = o.String("SpeedPatch") ?? (o.Flag("SpeedPatch") ? "" : "speed-x2");
        var outPath = Path.GetFullPath(o.String("Out") ?? Path.Combine(Path.GetTempPath(), "pes6-score-candidates.txt"));
        var patches = PatchSet.Load(o.String("File"));
        o.CheckAllUsed();
        if (speedPatch != "" && !patches.Contains(speedPatch)) throw new ToolException($"unknown patch '{speedPatch}'");
        if (length <= 0 || length % 4 != 0) throw new ToolException("-Length must be a positive multiple of 4");

        using var game = GameProcess.Open(processName, write: speedPatch != "");
        using var run = new Sampling();
        var before = speedPatch != "" ? patches.Snapshot(game, speedPatch) : default;
        if (speedPatch != "") patches.Set(game, speedPatch, on: true, force: true);
        var increments = new Dictionary<uint, int>();
        var bad = new HashSet<uint>();
        var events = new List<(uint Address, string Line)>();
        var presses = new List<string>();
        run.Start(0, beep: false);
        var clock = run.Clock;
        try
        {
            var prev = game.ReadOrZero(start, length);
            var prevPlayers = game.ReadOrZero(Players, PlayerCount * PlayerSize);
            var cur = new byte[length];
            double? frozen = null;
            double last = -100;
            while (run.Running(game, seconds))
            {
                var t0 = clock.ElapsedMilliseconds;
                var t = t0 / 1000.0;
                var (bx, _, bz) = Ball(game);
                if (!game.TryRead(start, cur)) Array.Clear(cur);
                for (var i = 0; i + 4 <= length; i += 4)
                {
                    int p = BitConverter.ToInt32(prev, i), c = BitConverter.ToInt32(cur, i);
                    if (p == c) continue;
                    var a = start + (uint)i;
                    if (c == p + 1 && c is >= 1 and <= 15)
                    {
                        increments[a] = increments.GetValueOrDefault(a) + 1;
                        events.Add((a, $"t={t,6:F1}  0x{a:X}: {p} -> {c}   ball x={bx:F0} z={bz:F0}"));
                    }
                    else bad.Add(a);
                }
                (prev, cur) = (cur, prev);

                var players = game.ReadOrZero(Players, PlayerCount * PlayerSize);
                double moved = 0;
                for (var i = 0; i < PlayerCount; i++)
                    moved += Math.Abs((double)BitConverter.ToSingle(players, i * PlayerSize + X) - BitConverter.ToSingle(prevPlayers, i * PlayerSize + X));
                prevPlayers = players;
                if (moved < 20) frozen ??= t; else frozen = null;
                var inNet = Math.Abs(bx) > 13500 && Math.Abs(bz) < 1500;
                if (skipScan > 0 && inNet && frozen != null && t - frozen > 3 && t - last > 4)
                {
                    var sent = SkipKey.Press(game, skipScan);
                    last = t;
                    presses.Add($"t={t:F1} skip key{(sent ? "" : " NOT sent (the game did not take the focus)")}, ball x={bx:F0} z={bz:F0}");
                }
                run.Pace(every, t0);
            }
        }
        finally { if (speedPatch != "" && !game.HasExited) patches.Restore(game, before); }

        var good = increments.Keys.Where(a => !bad.Contains(a)).ToHashSet();
        var lines = new List<string>
        {
            $"score probe: {(int)clock.Elapsed.TotalSeconds} s, region 0x{start:X} + 0x{length:X}, skip key pressed {presses.Count} time(s)",
        };
        lines.AddRange(presses);
        lines.Add("Candidates (only ever went up by 1, never anything else), by number of increments:");
        lines.AddRange(increments.Where(kv => good.Contains(kv.Key)).OrderByDescending(kv => kv.Value).Take(40)
            .Select(kv => $"  0x{kv.Key:X}: {kv.Value} increment(s)"));
        lines.Add("Increments of those candidates, with the ball:");
        lines.AddRange(events.Where(e => good.Contains(e.Address)).Select(e => e.Line));
        File.WriteAllLines(outPath, lines);
        foreach (var l in lines) Console.WriteLine(l);
        return 0;
    }
}
