using System.Globalization;
using static Pes6Research.GameLayout;

namespace Pes6Research;

/// <summary>
/// Automatic A/B test of a memory patch during a match nobody plays (CPU against CPU), at x2 speed (was pes6ab.ps1): waits for the
/// match to start, then alternates the game as it is (A) and with the patch (B) every -Phase seconds, logging the 22 players and the
/// ball the whole time, and prints the back line by ball zone for each. Everything is put back at the end (also on Ctrl+C or an error).
/// <para>Goal sequences wait for Start: after the ball has lain still in a goal (|x| &gt; -NetX, |z| &lt; 1300), and while nothing
/// moves or the players array is not a match's for -StuckAfter seconds, the key of scan code -SkipScan (1 = Esc, the user's Start) is
/// sent to the game, brought to the front (every press is listed in the summary). 0 turns that off. The samples taken before -Settle
/// seconds after a phase start or a resume are left out. The run stops after -GiveUpAfter seconds without a match.
/// -SpeedPatch '' keeps the game's own speed. Read-only apart from the named patches (research/patches.json).</para>
/// </summary>
static class AbCommand
{
    public const string Usage = "ab -Patch defence-line [-Phase 90] [-Cycles 3] [-Settle 10] [-SpeedPatch speed-x2] [-Every 250] [-Out ab.csv]\n" +
                                "   [-WaitMinutes 30] [-SkipScan 1] [-NetX 13500] [-StuckAfter 4] [-GiveUpAfter 240]\n" +
                                "                                                 A/B of a patch in a CPU-vs-CPU match: back line by ball zone";

    const uint LineWords = 0x3BCDCAE, FrameStep = 0x3BE1740;

    sealed record Row(long Ms, int Cycle, string Label, bool Settled, int Team, string Poss, double Ball, double Back4, double LineWord, float Step);

    public static int Run(Options o, string processName)
    {
        var patchName = o.String("Patch") ?? throw new ToolException("-Patch is required");
        var phase = o.Int("Phase", 90);
        var cycles = o.Int("Cycles", 3);
        var settle = o.Int("Settle", 10);
        var speedPatch = o.String("SpeedPatch") ?? (o.Flag("SpeedPatch") ? "" : "speed-x2"); // -SpeedPatch '' (or alone): no speed patch
        var every = o.Int("Every", 250);
        var waitMinutes = o.Int("WaitMinutes", 30);
        var skipScan = o.Int("SkipScan", 1);
        var netX = o.Int("NetX", 13500);
        var stuckAfter = o.Int("StuckAfter", 4);
        var giveUpAfter = o.Int("GiveUpAfter", 240);
        var outPath = Path.GetFullPath(o.String("Out") ?? Path.Combine(Path.GetTempPath(), "pes6-ab.csv"));
        var patches = PatchSet.Load(o.String("File"));
        o.CheckAllUsed();
        foreach (var n in new[] { patchName, speedPatch }.Where(n => n != "" && !patches.Contains(n))) throw new ToolException($"unknown patch '{n}'");

        using var game = GameProcess.Open(processName, write: true);
        using var run = new Sampling();
        var rows = new List<Row>();
        var pressLog = new List<string>();

        // 1. wait for a match
        Console.WriteLine($"Waiting for a match (up to {waitMinutes} min): start CPU against CPU in the game.");
        var deadline = DateTime.Now.AddMinutes(waitMinutes);
        while (!InMatch(game))
        {
            if (DateTime.Now > deadline) throw new ToolException("no match started");
            if (game.HasExited) throw new ToolException("the game closed");
            if (!run.Running(game, double.MaxValue)) return 1;
            Thread.Sleep(2000);
        }
        Thread.Sleep(5000); // past the kick-off set-up
        Sampling.Beep(1200, 300);
        Console.WriteLine("Match found.");

        // 2. the phases
        double? lost = null, netSince = null, goalAt = null, frozenSince = null;
        double lastPress = -100, resumedAt = 0, lastBx = 0;
        byte[]? prevAll = null;
        run.Start(0, beep: false);
        var clock = run.Clock;
        try
        {
            for (var c = 0; c < cycles; c++)
                foreach (var label in new[] { "A", "B" })
                {
                    patches.Set(game, patchName, on: label == "B", force: false);
                    if (speedPatch != "") patches.Set(game, speedPatch, on: true, force: true);
                    Console.WriteLine($"[{(int)clock.Elapsed.TotalSeconds}] cycle {c + 1}, phase {label}");
                    var phaseStart = clock.Elapsed.TotalSeconds;
                    while (clock.Elapsed.TotalSeconds - phaseStart < phase)
                    {
                        if (!run.Running(game, double.MaxValue)) throw new StopException(game.HasExited ? "the game closed" : "Ctrl+C");
                        var t0 = clock.ElapsedMilliseconds;
                        var now = clock.Elapsed.TotalSeconds;
                        var all = game.ReadOrZero(Players, PlayerCount * PlayerSize);
                        // nothing moves for a few seconds (celebration waiting, pause) or the players array is not a match's (replay,
                        // menu): press the skip key, at most every 4 s; give up when it lasts too long
                        var moved = 1e9;
                        if (prevAll != null)
                        {
                            moved = 0;
                            for (var i = 0; i < PlayerCount; i++)
                                moved += Math.Abs((double)F(all, i, X) - F(prevAll, i, X)) + Math.Abs((double)F(all, i, Z) - F(prevAll, i, Z));
                        }
                        prevAll = all;
                        if (moved < 20) frozenSince ??= now; else frozenSince = null;
                        var inMatch = InMatch(game);
                        if (inMatch) { if (lost != null) resumedAt = now; lost = null; }
                        else lost ??= now;
                        // only after a goal: the ball lying still in a goal (it rested at x = 13919 while a sequence waited for Start);
                        // throw-ins, free kicks and the other waits are left alone
                        var (bnx, _, bnz) = Ball(game);
                        if (Math.Abs(bnx) > netX && Math.Abs(bnz) < 1300 && Math.Abs(bnx - lastBx) < 5) netSince ??= now;
                        else if (Math.Abs(bnx) <= netX) netSince = null;
                        if (netSince != null && now - netSince > 2) goalAt = now;
                        lastBx = bnx;
                        var stuck = goalAt != null && now - goalAt < 90 &&
                                    (lost != null && now - lost > stuckAfter || frozenSince != null && now - frozenSince > stuckAfter);
                        if (stuck && skipScan > 0 && now - lastPress > 4)
                        {
                            var sent = SkipKey.Press(game, skipScan);
                            lastPress = now;
                            pressLog.Add($"t={now:F0} skip key{(sent ? "" : " NOT sent (the game did not take the focus)")}, ball x={bnx:F0} " +
                                         $"z={bnz:F0}, goal seen {now - goalAt:F0} s ago, lost={lost != null}, frozen={frozenSince != null}");
                        }
                        if (lost != null && now - lost > giveUpAfter) throw new StopException("the match ended");
                        if (!inMatch) { Thread.Sleep(every); continue; }

                        var carrier = game.ReadOrZero(Carrier, 1)[0];
                        var bx = Ball(game).X;
                        var words = game.ReadOrZero(LineWords, 4);
                        var step = BitConverter.ToSingle(game.ReadOrZero(FrameStep, 4));
                        var settled = now - phaseStart >= settle && now - resumedAt >= settle && frozenSince == null;
                        foreach (var t in new[] { 0, 1 })
                        {
                            var kx = F(all, t * 11, X);
                            var d = kx < 0 ? 1 : -1;
                            var xs = Enumerable.Range(1, 10).Select(i => d * (double)F(all, t * 11 + i, X)).Order().Take(4).Average();
                            var side = all[t * 11 * PlayerSize + 0x13];
                            var poss = carrier is < 1 or > 22 ? "none" : (carrier <= 11) == (t == 0) ? "own" : "opp";
                            var lineWord = side <= 1 ? d * (double)BitConverter.ToInt16(words, 2 * side) : double.NaN;
                            rows.Add(new Row(t0, c + 1, label, settled, t, poss, d * (double)bx, xs, lineWord, step));
                        }
                        run.Pace(every, t0);
                    }
                }
        }
        catch (StopException e) { Console.WriteLine($"Stopped: {e.Message}"); }
        finally
        {
            if (!game.HasExited)
            {
                patches.Set(game, patchName, on: false, force: false);
                if (speedPatch != "") patches.Set(game, speedPatch, on: false, force: true);
            }
            Sampling.End();
        }

        // 3. summary
        WriteCsv(outPath, rows);
        var use = rows.Where(r => r.Settled && r.Poss == "opp").ToList();
        Console.WriteLine($"{rows.Count / 2.0} samples ({use.Count} used: the side without the ball, settled) -> {outPath}");
        Console.WriteLine("game step seen: " + string.Join(", ", rows.Select(r => r.Step).Distinct().Order()) + $"; skip key pressed {pressLog.Count} time(s)");
        foreach (var p in pressLog) Console.WriteLine("  " + p);
        Console.WriteLine("Back four (mean x of the 4 deepest outfielders) and line word, own goal at -12400, by ball zone:");
        foreach (var z in use.GroupBy(r => (int)(Math.Floor(r.Ball / 4000) * 4000)).OrderBy(g => g.Key))
        {
            var a = z.Where(r => r.Label == "A").ToList();
            var b = z.Where(r => r.Label == "B").ToList();
            double aBack = Avg(a, r => r.Back4), aLine = Avg(a, r => r.LineWord), bBack = Avg(b, r => r.Back4), bLine = Avg(b, r => r.LineWord);
            Console.WriteLine($"ball {z.Key,6}..{z.Key + 4000,6}:  A back4={aBack,6:F0} line={aLine,6:F0} (n={a.Count,3})   " +
                              $"B back4={bBack,6:F0} line={bLine,6:F0} (n={b.Count,3})   B-A back4={bBack - aBack,5:F0} line={bLine - aLine,5:F0}");
        }
        return 0;
    }

    /// <summary>The keepers stand near their goals; in the menus the players array is empty.</summary>
    static bool InMatch(GameProcess game) =>
        Math.Abs(BitConverter.ToSingle(game.ReadOrZero(Player(1) + X, 4))) > 8000 &&
        Math.Abs(BitConverter.ToSingle(game.ReadOrZero(Player(12) + X, 4))) > 8000;

    static float F(byte[] all, int index, int offset) => BitConverter.ToSingle(all, index * PlayerSize + offset);

    static double Avg(List<Row> rows, Func<Row, double> f) => rows.Count > 0 ? rows.Average(f) : double.NaN;

    static void WriteCsv(string path, List<Row> rows)
    {
        using var w = new StreamWriter(path);
        w.WriteLine("\"ms\",\"cycle\",\"label\",\"settled\",\"team\",\"poss\",\"ball\",\"back4\",\"lineWord\",\"step\"");
        foreach (var r in rows)
            w.WriteLine(string.Join(",", new object[] { r.Ms, r.Cycle, r.Label, r.Settled, r.Team, r.Poss, r.Ball, r.Back4, r.LineWord, r.Step }
                .Select(v => $"\"{Convert.ToString(v, CultureInfo.InvariantCulture)}\"")));
    }

    sealed class StopException(string message) : Exception(message);
}
