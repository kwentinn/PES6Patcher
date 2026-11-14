using System.Globalization;

namespace Pes6Research;

/// <summary>
/// Defensive line height from a <c>positions</c> CSV (was pes6depth.ps1); the game need not run. For each sample and team,
/// oriented so that the own goal is at 0 and the opposite goal at 23600: ball depth, back line (mean of the 4 deepest
/// outfielders), deepest and highest outfielder, team length and width, split by who has the ball. Writes the per-sample
/// rows to &lt;in&gt;-&lt;field&gt;-teams.csv and prints the means by team, possession and ball zone.
/// </summary>
static class DepthCommand
{
    public const string Usage = "depth -In positions.csv [-Field p|t]             back line by ball zone from a positions CSV (t = the players' targets)";

    const double Goal = 11800, ZoneWidth = 3934;

    sealed record Row(int Ms, int Carrier, double Bx, int Id, int Team, int Slot, int State, double X, double Z);
    sealed record Sample(int Ms, int Team, string Poss, double Ball, double Line, double Deep, double High, double Length, double Width, int St7);

    public static int Run(Options o, string _)
    {
        var input = o.String("In") ?? (o.Positional.Count > 0 ? o.Positional[0] : throw new ToolException("-In is required"));
        var field = o.String("Field") ?? "p";
        o.CheckAllUsed();
        if (!File.Exists(input)) throw new ToolException($"{input}: no such file");

        var rows = ReadRows(input, field);
        var samples = new List<Sample>();
        foreach (var g in rows.GroupBy(r => r.Ms))
        {
            var sample = g.ToList();
            var carrier = sample[0].Carrier;
            var carrierTeam = carrier is >= 1 and <= 22 ? sample.FirstOrDefault(r => r.Id == carrier)?.Team ?? 0 : -1;
            foreach (var t in new[] { 0, 1 })
            {
                var team = sample.Where(r => r.Team == t).ToList();
                var keeper = team.FirstOrDefault(r => r.Slot == 0);
                if (keeper == null) continue;
                var dir = keeper.X < 0 ? 1 : -1;
                var outfield = team.Where(r => r.Slot != 0).ToList();
                var xs = outfield.Select(r => r.X * dir + Goal).Order().ToList();
                var zs = outfield.Select(r => r.Z).Order().ToList();
                if (xs.Count < 10) continue;
                samples.Add(new Sample(g.Key, t, carrierTeam == t ? "own" : carrierTeam >= 0 ? "opp" : "none",
                    sample[0].Bx * dir + Goal, xs.Take(4).Average(), xs[0], xs[9], xs[9] - xs[0], zs[9] - zs[0],
                    outfield.Count(r => r.State == 7)));
            }
        }

        var output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!,
            Path.GetFileNameWithoutExtension(input) + $"-{field}-teams.csv");
        using (var w = new StreamWriter(output))
        {
            w.WriteLine("\"ms\",\"team\",\"poss\",\"ball\",\"line\",\"deep\",\"high\",\"length\",\"width\",\"st7\"");
            foreach (var s in samples)
                w.WriteLine(string.Join(",", new object[] { s.Ms, s.Team, s.Poss, s.Ball, s.Line, s.Deep, s.High, s.Length, s.Width, s.St7 }
                    .Select(v => $"\"{Convert.ToString(v, CultureInfo.InvariantCulture)}\"")));
        }

        Console.WriteLine($"field={field}, samples={samples.Count / 2.0}");
        Console.WriteLine("Ball zone (thirds of the pitch from the own goal), by team and possession: mean back line, line - ball, length, width, n");
        foreach (var g in samples.GroupBy(s => $"{s.Team}, {s.Poss}, {Math.Min(5, Math.Max(0, Math.Floor(s.Ball / ZoneWidth)))}")
                                 .OrderBy(g => g.Key, StringComparer.InvariantCulture))
        {
            var m = g.ToList();
            Console.WriteLine($"{g.Key,-14} line={m.Average(s => s.Line),6:F0} ball={m.Average(s => s.Ball),6:F0} " +
                              $"line-ball={m.Average(s => s.Line - s.Ball),6:F0} deep={m.Average(s => s.Deep),6:F0} " +
                              $"length={m.Average(s => s.Length),6:F0} width={m.Average(s => s.Width),6:F0} " +
                              $"st7={m.Average(s => s.St7),4:F1} n={m.Count}");
        }
        return 0;
    }

    static List<Row> ReadRows(string path, string field)
    {
        using var r = new StreamReader(path);
        var header = (r.ReadLine() ?? throw new ToolException($"{path}: empty")).Split(',');
        int Col(string name) => Array.IndexOf(header, name) is var i and >= 0 ? i : throw new ToolException($"{path}: no column '{name}'");
        int ms = Col("ms"), carrier = Col("carrier"), bx = Col("bx"), id = Col("id"), team = Col("team"), slot = Col("slot"),
            state = Col("state"), x = Col(field + "x"), z = Col(field + "z");
        var rows = new List<Row>();
        for (var line = r.ReadLine(); line != null; line = r.ReadLine())
        {
            if (line.Length == 0) continue;
            var c = line.Split(',');
            rows.Add(new Row(I(c[ms]), I(c[carrier]), D(c[bx]), I(c[id]), I(c[team]), I(c[slot]), I(c[state]), D(c[x]), D(c[z])));
        }
        return rows;

        static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);
        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    }
}
