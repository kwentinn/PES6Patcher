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

    internal sealed record Row(int Ms, int Carrier, double Bx, int Id, int Team, int Slot, int State, double X, double Z);
    internal sealed record Sample(int Ms, int Team, string Poss, double Ball, double Line, double Deep, double High, double Length, double Width, int St7);

    public static int Run(Options o, string _)
    {
        var input = o.String("In") ?? (o.Positional.Count > 0 ? o.Positional[0] : throw new ToolException("-In is required"));
        var field = o.String("Field") ?? "p";
        o.CheckAllUsed();
        if (!File.Exists(input)) throw new ToolException($"{input}: no such file");

        List<Row> rows; int skipped;
        using (var r = new StreamReader(input)) (rows, skipped) = ReadRows(r, field, input);
        if (skipped > 0) Console.Error.WriteLine($"{skipped} unreadable line(s) left out (a run cut short?)");
        var samples = Compute(rows);

        var output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!,
            Path.GetFileNameWithoutExtension(input) + $"-{field}-teams.csv");
        using (var w = new StreamWriter(output)) WriteCsv(w, samples);

        Console.WriteLine($"field={field}, samples={samples.Count / 2.0}");
        foreach (var line in Summary(samples)) Console.WriteLine(line);
        return 0;
    }

    /// <summary>Per sample and team: the line, ball, length and width oriented from the own goal (told by the keeper's side).</summary>
    internal static List<Sample> Compute(IEnumerable<Row> rows)
    {
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
        return samples;
    }

    internal static void WriteCsv(TextWriter w, IEnumerable<Sample> samples)
    {
        w.WriteLine("\"ms\",\"team\",\"poss\",\"ball\",\"line\",\"deep\",\"high\",\"length\",\"width\",\"st7\"");
        foreach (var s in samples)
            w.WriteLine(string.Join(",", new object[] { s.Ms, s.Team, s.Poss, s.Ball, s.Line, s.Deep, s.High, s.Length, s.Width, s.St7 }
                .Select(v => $"\"{Convert.ToString(v, CultureInfo.InvariantCulture)}\"")));
    }

    /// <summary>The means by team, possession and ball zone (sixths of the pitch from the own goal, 0-5).</summary>
    internal static IEnumerable<string> Summary(List<Sample> samples)
    {
        yield return "Ball zone (sixths of the pitch from the own goal, 0-5), by team and possession: mean back line, line - ball, length, width, n";
        foreach (var g in samples.GroupBy(s => $"{s.Team}, {s.Poss}, {Zone(s.Ball)}").OrderBy(g => g.Key, StringComparer.InvariantCulture))
        {
            var m = g.ToList();
            yield return $"{g.Key,-14} line={m.Average(s => s.Line),6:F0} ball={m.Average(s => s.Ball),6:F0} " +
                         $"line-ball={m.Average(s => s.Line - s.Ball),6:F0} deep={m.Average(s => s.Deep),6:F0} " +
                         $"length={m.Average(s => s.Length),6:F0} width={m.Average(s => s.Width),6:F0} " +
                         $"st7={m.Average(s => s.St7),4:F1} n={m.Count}";
        }
    }

    internal static int Zone(double ball) => (int)Math.Min(5, Math.Max(0, Math.Floor(ball / ZoneWidth)));

    /// <summary>The rows of a positions CSV; lines that cannot be read (a run cut short mid-line) are left out and counted.</summary>
    internal static (List<Row> Rows, int Skipped) ReadRows(TextReader r, string field, string source)
    {
        var header = (r.ReadLine() ?? throw new ToolException($"{source}: empty")).Split(',');
        int Col(string name) => Array.IndexOf(header, name) is var i and >= 0 ? i : throw new ToolException($"{source}: no column '{name}'");
        int ms = Col("ms"), carrier = Col("carrier"), bx = Col("bx"), id = Col("id"), team = Col("team"), slot = Col("slot"),
            state = Col("state"), x = Col(field + "x"), z = Col(field + "z");
        var width = new[] { ms, carrier, bx, id, team, slot, state, x, z }.Max() + 1;
        var rows = new List<Row>();
        var skipped = 0;
        for (var line = r.ReadLine(); line != null; line = r.ReadLine())
        {
            if (line.Length == 0) continue;
            var c = line.Split(',');
            if (c.Length < width || !I(c[ms], out var vMs) || !I(c[carrier], out var vCarrier) || !D(c[bx], out var vBx) ||
                !I(c[id], out var vId) || !I(c[team], out var vTeam) || !I(c[slot], out var vSlot) || !I(c[state], out var vState) ||
                !D(c[x], out var vX) || !D(c[z], out var vZ)) { skipped++; continue; }
            rows.Add(new Row(vMs, vCarrier, vBx, vId, vTeam, vSlot, vState, vX, vZ));
        }
        return (rows, skipped);

        static bool I(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        static bool D(string s, out double v) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
