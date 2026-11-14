using System.Globalization;
using static Pes6Research.GameLayout;

namespace Pes6Research;

/// <summary>
/// Team shape while not in possession (was pes6shape.ps1): samples the 22 players, then prints, by ball zone and in each side's
/// own direction (0 = centre, negative = own half): mean x of the defenders (role +0x10 1-7), midfielders (8-0x17) and forwards
/// (0x18+), the gaps between those lines, the block length and width, and the spread of each line. Read-only; the line is only
/// worth reading while the opponent has the ball. The team is told by the player's index (1-11, 12-22), not by +0x12.
/// </summary>
static class ShapeCommand
{
    public const string Usage = "shape [-Seconds 90] [-Every 250] [-Out shape.csv]\n" +
                                "                                                 team shape out of possession by ball zone: lines, gaps, length, width";

    sealed record Sample(string Poss, double Ball, double Def, double Mid, double Fwd, double SdDef, double SdMid,
        double Length, double Width, string Roles);

    public static int Run(Options o, string processName)
    {
        var seconds = o.Int("Seconds", 90);
        var every = o.Int("Every", 250);
        var outPath = o.String("Out");
        o.CheckAllUsed();

        var samples = new List<Sample>();
        using (var game = GameProcess.Open(processName))
        using (var run = new Sampling())
        {
            run.Start(0, beep: false);
            var all = new byte[PlayerCount * PlayerSize];
            while (run.Running(game, seconds))
            {
                var ms = run.Clock.ElapsedMilliseconds;
                if (game.TryRead(Players, all)) samples.AddRange(Take(all, game.ReadOrZero(Carrier, 1)[0], Ball(game).X));
                run.Pace(every, ms);
            }
        }

        if (outPath != null) WriteCsv(outPath, samples);
        Console.WriteLine($"samples: {samples.Count / 2.0}; roles seen: " +
            string.Join(" | ", samples.Select(s => s.Roles).Distinct(StringComparer.InvariantCultureIgnoreCase)
                                      .Order(StringComparer.InvariantCultureIgnoreCase).Take(4)));
        foreach (var g in samples.Where(s => s.Poss == "opp").GroupBy(s => (int)(Math.Floor(s.Ball / 4000) * 4000)).OrderBy(g => g.Key))
        {
            var m = g.ToList();
            double def = Avg(m.Select(s => s.Def)), mid = Avg(m.Select(s => s.Mid)), fwd = Avg(m.Select(s => s.Fwd));
            Console.WriteLine($"ball {g.Key,6}..{g.Key + 4000,6}: def={def,6:F0} mid={mid,6:F0} fwd={fwd,6:F0} | mid-def={mid - def,5:F0} " +
                              $"fwd-mid={fwd - mid,5:F0} | length={Avg(m.Select(s => s.Length)),5:F0} width={Avg(m.Select(s => s.Width)),5:F0} | " +
                              $"sdDef={Avg(m.Select(s => s.SdDef)),4:F0} sdMid={Avg(m.Select(s => s.SdMid)),4:F0} n={m.Count}");
        }
        return 0;
    }

    static IEnumerable<Sample> Take(byte[] all, byte carrier, float bx)
    {
        foreach (var t in new[] { 0, 1 })
        {
            var kx = BitConverter.ToSingle(all, t * 11 * PlayerSize + X);
            if (kx == 0) continue;
            var d = kx < 0 ? 1 : -1;
            var poss = carrier is < 1 or > 22 ? "none" : (carrier <= 11) == (t == 0) ? "own" : "opp";
            List<double> def = [], mid = [], fwd = [], xs = [], zs = [];
            var roles = new List<byte>();
            for (var i = 1; i <= 10; i++)
            {
                var p = (t * 11 + i) * PlayerSize;
                var x = d * (double)BitConverter.ToSingle(all, p + X);
                xs.Add(x); zs.Add(BitConverter.ToSingle(all, p + Z));
                var r = all[p + Role]; roles.Add(r);
                (r <= 7 ? def : r < 0x18 ? mid : fwd).Add(x);
            }
            xs.Sort(); zs.Sort();
            yield return new Sample(poss, d * (double)bx, Avg(def), Avg(mid), Avg(fwd), Sd(def), Sd(mid), xs[9] - xs[0], zs[9] - zs[0],
                string.Join(' ', roles.Select(r => r.ToString("X"))));
        }
    }

    static double Avg(IEnumerable<double> v) { var l = v.ToList(); return l.Count > 0 ? l.Average() : double.NaN; }

    /// <summary>Population standard deviation; 0 under two values.</summary>
    static double Sd(List<double> v)
    {
        if (v.Count < 2) return 0;
        var m = v.Average();
        return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / v.Count);
    }

    static void WriteCsv(string path, List<Sample> samples)
    {
        using var w = new StreamWriter(path);
        w.WriteLine("\"poss\",\"ball\",\"def\",\"mid\",\"fwd\",\"sdDef\",\"sdMid\",\"length\",\"width\",\"roles\"");
        foreach (var s in samples)
            w.WriteLine(string.Join(",", new object[] { s.Poss, s.Ball, s.Def, s.Mid, s.Fwd, s.SdDef, s.SdMid, s.Length, s.Width, s.Roles }
                .Select(v => $"\"{Convert.ToString(v, CultureInfo.InvariantCulture)}\"")));
    }
}
