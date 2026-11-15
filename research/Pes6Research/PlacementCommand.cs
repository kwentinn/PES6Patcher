using System.Globalization;

namespace Pes6Research;

/// <summary>
/// Off-ball placement in a <c>positions</c> log of a match (first written 2026-11-15 for the "chaos" and crosses complaints):
/// each player's average place, role and placement modes; crosses (where the team-mates and the strikers are when the ball comes
/// down); the crowd around the ball carrier and the balls won by a challenge; facing against run; target jumps by mode; the most
/// defensive midfielder against the back four. When the log has a <c>patch</c> column (<c>positions -Patch</c>), the crosses, the
/// crowd and the challenges are also given for phase A (patch off) and B (patch on). 1 m is about 233 units; the ball's y grows
/// downwards (about -23 on the ground).
/// </summary>
static class PlacementCommand
{
    public const string Usage = "placement -In positions.csv                      off-ball placement of a positions log: crosses, crowd, challenges, facing, A/B";

    internal sealed record P(int Id, int Slot, int State, double X, double Z, double Tx, double Tz, int Speed, int Role, int Mode,
        double Facing, double Run, double Heading);
    internal sealed record S(long Ms, int Carrier, double Bx, double Bz, double Height, string Phase, P[] Ps);

    const double GoalX = 12400, BoxDepth = 3850, BoxHalf = 4700, M5 = 1165, M10 = 2330;

    public static int Run(Options o, string processName)
    {
        var inPath = o.String("In") ?? throw new ToolException("-In is required");
        o.CheckAllUsed();
        var samples = Read(inPath);
        Console.WriteLine($"{samples.Count} match samples");
        if (samples.Count == 0) return 1;
        Report(samples, Console.Out);
        return 0;
    }

    /// <summary>The match samples of a positions log (22 rows each; both keepers in their own halves).</summary>
    internal static List<S> Read(string path)
    {
        if (!File.Exists(path)) throw new ToolException($"{path}: not found");
        var lines = File.ReadAllLines(path);
        var h = lines[0].Split(',');
        int C(string n) => Array.IndexOf(h, n) is var i and >= 0 ? i : throw new ToolException($"{path}: no column '{n}' (log it again with pes6r positions)");
        int patch = Array.IndexOf(h, "patch");
        var inv = CultureInfo.InvariantCulture;
        double D(string[] c, string n) => double.Parse(c[C(n)], inv);
        int I(string[] c, string n) => int.Parse(c[C(n)], inv);
        var samples = new List<S>();
        foreach (var g in lines.Skip(1).Select(l => l.Split(',')).GroupBy(c => c[C("ms")]))
        {
            var rows = g.ToList();
            if (rows.Count != 22) continue;
            var ps = rows.Select(c => new P(I(c, "id"), I(c, "slot"), I(c, "state"), D(c, "px"), D(c, "pz"), D(c, "tx"), D(c, "tz"), I(c, "speed"),
                I(c, "role"), I(c, "mode"), D(c, "facing"), D(c, "run"), D(c, "heading"))).ToArray();
            var f = rows[0];
            samples.Add(new S(long.Parse(f[C("ms")], inv), I(f, "carrier"), D(f, "bx"), D(f, "bz"), -D(f, "by"), patch >= 0 ? f[patch] : "", ps));
        }
        return samples.Where(s => Math.Abs(s.Ps[0].X) > 6000 && Math.Abs(s.Ps[11].X) > 6000 && Math.Sign(s.Ps[0].X) != Math.Sign(s.Ps[11].X)).ToList();
    }

    static double Diff(double a, double b) { var d = Math.Abs(a - b) % 360; return d > 180 ? 360 - d : d; }
    /// <summary>+1 when the side (0: ids 1-11, 1: ids 12-22) attacks towards +x, by its keeper's half (they swap at half-time).</summary>
    static int Dir(S s, int side) => -Math.Sign(s.Ps[side * 11].X);
    static int Side(int id) => id <= 11 ? 0 : 1;
    static double Dist(double x0, double z0, double x1, double z1) => Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));

    internal static void Report(List<S> samples, TextWriter w)
    {
        var phases = samples.Select(s => s.Phase).Where(p => p != "").Distinct().Order().ToList();

        w.WriteLine("id role(s)  meanX(attack+)  meanZ  modes");
        for (int i = 0; i < 22; i++)
        {
            var xs = samples.Average(s => s.Ps[i].X * Dir(s, Side(i + 1)));
            var zs = samples.Average(s => s.Ps[i].Z);
            var roles = string.Join("/", samples.Select(s => s.Ps[i].Role).Distinct().Take(3));
            var modes = string.Join(" ", samples.GroupBy(s => s.Ps[i].Mode).OrderByDescending(g => g.Count()).Take(4).Select(g => $"{g.Key:X2}:{100 * g.Count() / samples.Count}%"));
            w.WriteLine($"{i + 1,2} {roles,-8} {xs,7:F0} {zs,7:F0}  {modes}");
        }

        // crosses: a carrier wide in the last ~19 m (not a corner) loses the ball, and it rises within 0.5 s
        var crosses = new List<(string Phase, int InBox, double Closest, double Striker, string Line)>();
        for (int k = 1; k < samples.Count - 30; k++)
        {
            var a = samples[k - 1];
            if (a.Carrier is < 1 or > 22) continue;
            var side = Side(a.Carrier); var d = Dir(a, side);
            var cp = a.Ps[a.Carrier - 1];
            if (cp.X * d < GoalX - 4500 || Math.Abs(cp.Z) < 3500 || cp.State is 47 or 48) continue;
            var b = samples[k];
            if (b.Carrier == a.Carrier) continue;
            int rise = samples.Skip(k).Take(10).ToList().FindIndex(s => s.Height > 150);
            if (rise < 0) continue;
            var land = samples.Skip(k + rise).Take(60).FirstOrDefault(s => s.Height < 60) ?? samples[Math.Min(k + 30, samples.Count - 1)];
            var mates = land.Ps.Where(p => Side(p.Id) == side && p.Slot != 0 && p.Id != a.Carrier).ToList();
            double DL(P p) => Dist(p.X, p.Z, land.Bx, land.Bz);
            var inBox = mates.Count(p => p.X * d > GoalX - BoxDepth && Math.Abs(p.Z) < BoxHalf);
            var closest = mates.OrderBy(DL).First();
            var strikers = mates.Where(p => p.Role >= 33).ToList();
            var striker = strikers.Count > 0 ? strikers.Min(DL) : double.NaN;
            var foe = land.Ps.Where(p => Side(p.Id) != side && p.Slot != 0).Min(DL);
            var landsInBox = land.Bx * d > GoalX - BoxDepth && Math.Abs(land.Bz) < BoxHalf;
            crosses.Add((a.Phase, inBox, DL(closest), striker,
                $"t={b.Ms / 1000.0:F1}{(a.Phase != "" ? " " + a.Phase : "")} side{side} from ({cp.X * d:F0},{cp.Z:F0}) lands ({land.Bx * d:F0},{land.Bz:F0}){(landsInBox ? " in box" : "")}: " +
                $"{inBox} in box, closest mate {DL(closest):F0} (id{closest.Id} role{closest.Role}), closest foe {foe:F0} | " +
                string.Join(" | ", strikers.Select(p => $"striker id{p.Id} ->ball {DL(p):F0} t({p.Tx * d:F0},{p.Tz:F0}) m{p.Mode:X2} st{p.State}"))));
            k += 40;
        }
        void CrossSummary(string label, List<(string Phase, int InBox, double Closest, double Striker, string Line)> c) =>
            w.WriteLine($"  {label}: {c.Count} crosses, {(c.Count > 0 ? c.Average(x => x.InBox) : 0):F2} team-mates in the box at the landing, " +
                        $"no team-mate within 6 m {c.Count(x => x.Closest > 1500)}, median striker distance to the landing " +
                        $"{(c.Any(x => !double.IsNaN(x.Striker)) ? c.Where(x => !double.IsNaN(x.Striker)).Select(x => x.Striker).Order().ElementAt(c.Count(x => !double.IsNaN(x.Striker)) / 2) : double.NaN):F0}");
        w.WriteLine("\ncrosses (the landing = the ball's first return near the ground after it rose):");
        CrossSummary("all", crosses);
        foreach (var ph in phases) CrossSummary($"phase {ph}", crosses.Where(c => c.Phase == ph).ToList());
        foreach (var c in crosses) w.WriteLine("    " + c.Line);

        // the crowd around the ball carrier, and the balls won by a challenge (the new carrier, of the other side, was within 3 m)
        void Crowd(string label, List<S> sel)
        {
            int k5m = 0, k5o = 0, k10m = 0, k10o = 0, with = 0, won = 0;
            var states5 = new Dictionary<string, int>();
            for (int k = 0; k < sel.Count; k++)
            {
                var s = sel[k];
                if (s.Carrier is < 1 or > 22) continue;
                var c = s.Ps[s.Carrier - 1]; with++;
                foreach (var p in s.Ps)
                {
                    if (p.Id == c.Id || p.Slot == 0) continue;
                    var dd = Dist(p.X, p.Z, c.X, c.Z);
                    bool mate = Side(p.Id) == Side(c.Id);
                    if (dd < M10) { if (mate) k10m++; else k10o++; }
                    if (dd >= M5) continue;
                    if (mate) k5m++; else k5o++;
                    var key = $"{(mate ? "mate" : "foe")} st{p.State} m{p.Mode:X2}";
                    states5[key] = states5.GetValueOrDefault(key) + 1;
                }
                // the carrier loses the ball (often loose for a moment), and within 0.5 s an opponent who stood within 3 m of him has it
                if (k + 1 < sel.Count && sel[k + 1].Carrier != s.Carrier)
                {
                    var next = sel.Skip(k + 1).Take(10).TakeWhile(n => n.Ms - s.Ms < 600).FirstOrDefault(n => n.Carrier is >= 1 and <= 22);
                    if (next != null && Side(next.Carrier) != Side(s.Carrier) &&
                        Dist(s.Ps[next.Carrier - 1].X, s.Ps[next.Carrier - 1].Z, c.X, c.Z) < 700) won++;
                }
            }
            if (with == 0) return;
            var seconds = with * 0.05;
            w.WriteLine($"  {label}: {with} samples with a carrier; within 5 m {(double)k5m / with:F2} team-mates + {(double)k5o / with:F2} opponents, " +
                        $"within 10 m {(double)k10m / with:F2} + {(double)k10o / with:F2}; balls won by a challenge {won} ({won / seconds * 60:F1} per minute of possession, real time)");
            if (label == "all")
                foreach (var kv in states5.OrderByDescending(kv => kv.Value).Take(8)) w.WriteLine($"      within 5 m: {kv.Key} {100.0 * kv.Value / with:F0}% of samples");
        }
        w.WriteLine("\naround the ball carrier:");
        Crowd("all", samples);
        foreach (var ph in phases) Crowd($"phase {ph}", samples.Where(s => s.Phase == ph).ToList());

        w.WriteLine("\nattack support, passes and reactions (moving = more than 100 units in 0.25 s, about 1.7 m/s at x2):");
        Play("all", samples);
        foreach (var ph in phases) Play($"phase {ph}", samples.Where(s => s.Phase == ph).ToList());
        void Play(string label, List<S> sel)
        {
            // where a player goes over the next 5 samples (0.25 s), along his attacking direction (x) and across (z)
            (double Ahead, double Across)? Move(int k, int i, int side)
            {
                if (k + 5 >= sel.Count || sel[k + 5].Ms - sel[k].Ms > 400) return null;
                var d = Dir(sel[k], side);
                return ((sel[k + 5].Ps[i].X - sel[k].Ps[i].X) * d, sel[k + 5].Ps[i].Z - sel[k].Ps[i].Z);
            }

            // support: the carrier in the opponents' half; his outfield team-mates within 15 m, ahead of the ball, and running forward
            int with = 0, near = 0, ahead = 0, forward = 0, back = 0, still = 0, mates = 0;
            for (int k = 0; k < sel.Count; k++)
            {
                var s = sel[k];
                if (s.Carrier is < 1 or > 22) continue;
                int side = Side(s.Carrier), d = Dir(s, side);
                var c = s.Ps[s.Carrier - 1];
                if (c.X * d < 0) continue;
                with++;
                for (int i = side * 11 + 1; i < side * 11 + 11; i++)
                {
                    if (i == s.Carrier - 1) continue;
                    var p = s.Ps[i];
                    if (Dist(p.X, p.Z, c.X, c.Z) < 3500) near++;
                    if (p.X * d > c.X * d) ahead++;
                    if (Move(k, i, side) is not { } m) continue;
                    mates++;
                    if (m.Ahead > 100) forward++; else if (m.Ahead < -100) back++; else if (Math.Abs(m.Across) < 100) still++;
                }
            }
            if (with > 0 && mates > 0)
                w.WriteLine($"  {label}: support (carrier in the opponents' half, {with} samples): {(double)near / with:F2} team-mates within 15 m, " +
                            $"{(double)ahead / with:F2} ahead of the ball; team-mates running forward {100.0 * forward / mates:F0}%, back {100.0 * back / mates:F0}%, standing {100.0 * still / mates:F0}%");

            // releases: the carrier lets the ball go (not taken by a challenge); who has it next within 3 s
            int kept = 0, lost = 0; var reach = new List<double>();
            for (int k = 0; k + 1 < sel.Count; k++)
            {
                var s = sel[k];
                if (s.Carrier is < 1 or > 22 || sel[k + 1].Carrier == s.Carrier) continue;
                var c = s.Ps[s.Carrier - 1];
                if (c.State is 47 or 48) continue;   // set pieces
                var next = sel.Skip(k + 1).Take(60).TakeWhile(n => n.Ms - s.Ms < 3200).FirstOrDefault(n => n.Carrier is >= 1 and <= 22 && n.Carrier != s.Carrier);
                if (next == null) continue;
                var taker = s.Ps[next.Carrier - 1];
                bool sameSide = Side(next.Carrier) == Side(s.Carrier);
                if (!sameSide && Dist(taker.X, taker.Z, c.X, c.Z) < 700) continue;   // a challenge, not a release
                if (sameSide) { kept++; reach.Add((next.Ms - s.Ms) / 1000.0); } else lost++;
            }
            if (kept + lost > 0)
                w.WriteLine($"  {label}: releases (passes, crosses, clearances): {kept + lost}, a team-mate has it next {100.0 * kept / (kept + lost):F0}%, " +
                            $"an opponent {100.0 * lost / (kept + lost):F0}%; median time to the team-mate {(reach.Count > 0 ? reach.Order().ElementAt(reach.Count / 2) : 0):F2} s (real, x2 game)");

            // reactions: after the ball changes side, how long until each outfield player's run turns by more than 60°
            var delays = new List<double>[2] { [], [] };   // 0: the side that lost the ball, 1: the side that won it
            for (int k = 6; k + 1 < sel.Count; k++)
            {
                if (sel[k].Carrier is < 1 or > 22) continue;
                var prev = sel.Take(k).Reverse().Take(60).FirstOrDefault(n => n.Carrier is >= 1 and <= 22);
                if (prev == null || Side(prev.Carrier) == Side(sel[k].Carrier)) continue;
                int winner = Side(sel[k].Carrier);
                for (int i = 0; i < 22; i++)
                {
                    if (sel[k].Ps[i].Slot == 0) continue;
                    var before = Move(k - 6, i, 0);
                    if (before is not { } b || Math.Abs(b.Ahead) + Math.Abs(b.Across) < 150) continue;
                    for (int t = k; t < k + 40; t++)
                    {
                        if (Move(t, i, 0) is not { } a) break;
                        if (Math.Abs(a.Ahead) + Math.Abs(a.Across) < 150) continue;
                        var cos = (a.Ahead * b.Ahead + a.Across * b.Across) / (Math.Sqrt(a.Ahead * a.Ahead + a.Across * a.Across) * Math.Sqrt(b.Ahead * b.Ahead + b.Across * b.Across));
                        if (cos < 0.5) { delays[Side(i + 1) == winner ? 1 : 0].Add((sel[t].Ms - sel[k].Ms) / 1000.0); break; }
                    }
                }
                k += 40;
            }
            string Med(List<double> v) => v.Count == 0 ? "-" : $"{v.Order().ElementAt(v.Count / 2):F2} s (n={v.Count})";
            w.WriteLine($"  {label}: reaction after the ball changes side (median until a run turns by > 60 deg, real time at x2): " +
                        $"side that lost it {Med(delays[0])}, side that won it {Med(delays[1])}");
        }

        // facing against run and target jumps, off the ball
        var facing = new List<double>(); var aimed = new List<double>();
        var jumpBy = new Dictionary<string, int>(); var allBy = new Dictionary<string, int>();
        var backBy = new Dictionary<string, int>(); var moveBy = new Dictionary<string, int>();
        int reversals = 0, runs = 0, steadyJumps = 0;
        for (int k = 1; k < samples.Count; k++)
            for (int i = 0; i < 22; i++)
            {
                var p0 = samples[k - 1].Ps[i]; var p1 = samples[k].Ps[i];
                if (p1.Slot == 0 || p1.Id == samples[k].Carrier) continue;
                var key = $"m{p1.Mode:X2} st{p1.State}";
                allBy[key] = allBy.GetValueOrDefault(key) + 1;
                if (Dist(p0.Tx, p0.Tz, p1.Tx, p1.Tz) > M10)
                {
                    jumpBy[key] = jumpBy.GetValueOrDefault(key) + 1;
                    if (k >= 20 && samples[k].Carrier is >= 1 and <= 22 && Enumerable.Range(k - 20, 21).All(j => samples[j].Carrier == samples[k].Carrier)) steadyJumps++;
                }
                if (p1.Speed <= 600) continue;
                facing.Add(Diff(p1.Facing, p1.Run)); aimed.Add(Diff(p1.Run, p1.Heading));
                moveBy[key] = moveBy.GetValueOrDefault(key) + 1;
                if (Diff(p1.Facing, p1.Run) >= 90) backBy[key] = backBy.GetValueOrDefault(key) + 1;
                if (k >= 10 && samples[k - 10].Ps[i].Speed > 600) { runs++; if (Diff(p1.Run, samples[k - 10].Ps[i].Run) > 120) reversals++; }
            }
        string Share(List<double> v) => v.Count == 0 ? "-" :
            $"<20 deg {100.0 * v.Count(x => x < 20) / v.Count:F0}%, 20-90 deg {100.0 * v.Count(x => x is >= 20 and < 90) / v.Count:F0}%, >=90 deg {100.0 * v.Count(x => x >= 90) / v.Count:F0}%";
        w.WriteLine($"\noff the ball, moving: facing vs run {Share(facing)}; run vs aimed {Share(aimed)}; run turned > 120 deg within 0.5 s {100.0 * reversals / Math.Max(runs, 1):F1}%");
        w.WriteLine("  facing >= 90 deg from the run, by mode/state:");
        foreach (var kv in backBy.OrderByDescending(kv => kv.Value).Take(6))
            w.WriteLine($"    {kv.Key,-10} {100.0 * kv.Value / moveBy[kv.Key]:F0}% of its moving samples");
        w.WriteLine($"  target jumps > 10 m in 50 ms: {jumpBy.Values.Sum()}, of which while the same carrier kept the ball 1 s: {steadyJumps}");
        foreach (var kv in jumpBy.OrderByDescending(kv => kv.Value).Take(5))
            w.WriteLine($"    {kv.Key,-10} {100.0 * kv.Value / allBy[kv.Key]:F1}% of its samples");

        // the most defensive midfielder (5th deepest outfielder on average) against the back four, by ball zone
        w.WriteLine("\ndefensive midfielder against the 4 deepest outfielders, by ball zone:");
        foreach (var side in new[] { 0, 1 })
        {
            var idx = Enumerable.Range(side * 11 + 1, 10).OrderBy(i => samples.Average(s => s.Ps[i].X * Dir(s, side))).ToList();
            var dm = idx[4];
            foreach (var (lo, hi, name) in new[] { (-13000, -4000, "ball in own third"), (-4000, 4000, "ball in the middle"), (4000, 13000, "ball in last third") })
            {
                var sel = samples.Where(s => s.Bx * Dir(s, side) >= lo && s.Bx * Dir(s, side) < hi).ToList();
                if (sel.Count == 0) continue;
                var gap = sel.Average(s => s.Ps[dm].X * Dir(s, side) - idx.Take(4).Average(i => s.Ps[i].X * Dir(s, side)));
                var ballZ = sel.Average(s => Math.Abs(s.Ps[dm].Z - s.Bz));
                var toTarget = sel.Average(s => Dist(s.Ps[dm].X, s.Ps[dm].Z, s.Ps[dm].Tx, s.Ps[dm].Tz));
                w.WriteLine($"  side{side} id{dm + 1} {name,-19}: {gap,6:F0} ahead of the back four, {ballZ,5:F0} from the ball across, {toTarget,5:F0} from his target (n={sel.Count})");
            }
        }
    }
}
