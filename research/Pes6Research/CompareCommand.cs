using System.Globalization;

namespace Pes6Research;

/// <summary>
/// The same measures on <c>positions</c> logs of the game and on real matches (2026-11-16), in metres and seconds of match time,
/// side by side: off-ball speeds, attack support, the crowd around the player on the ball, team shape, passing, challenges,
/// reactions when the ball changes side, crosses. A log with the <c>patch</c> column (<c>positions -Patch</c>) gives one column
/// per phase (A: patch off, B: on). The real columns come from research/real-matches.csv; <c>-Metrica</c> computes them again
/// from the Metrica Sports sample data (github.com/metrica-sports/sample-data: Sample_Game_N_RawTrackingData_Home_Team.csv,
/// ..._Away_Team.csv and ..._RawEventsData.csv in one folder; 25 Hz, coordinates 0..1 on 105 x 68 m) and
/// <c>-SaveReference</c> writes them back to that file.
/// <para>The player on the ball is the game's carrier id in a log; in a real match he is taken from the events: from the moment
/// a player gets the ball (a pass received, a recovery) to his next pass, shot or loss. Set pieces are left out on both sides.</para>
/// </summary>
static class CompareCommand
{
    public static readonly string[] Switches = ["SaveReference"];
    public const string Usage = "compare -In a.csv,b.csv [-Speed 2] [-Metrica folder] [-Games 1,2] [-SaveReference] [-Reference real-matches.csv]\n" +
                                "                                                 positions logs against real matches: speeds, support, passing, reactions, crosses";

    public static int Run(Options o, string processName)
    {
        var inputs = o.List("In");
        var speed = o.Int("Speed", 2);
        var metrica = o.String("Metrica");
        var games = o.List("Games");
        var save = o.Flag("SaveReference");
        var referencePath = o.String("Reference");
        o.CheckAllUsed();
        if (inputs.Count == 0 && metrica == null) throw new ToolException("-In (positions logs) or -Metrica (real matches) is required");
        if (save && metrica == null) throw new ToolException("-SaveReference needs -Metrica");

        var columns = new List<(string Name, List<KeyValuePair<string, string>> Values)>();
        foreach (var path in inputs)
            foreach (var d in MatchLoader.Pes(path, speed))
                columns.Add((d.Name, MatchMeasures.Run(d)));
        var keys = columns.Count > 0 ? columns[0].Values.Select(kv => kv.Key).ToList() : null;

        var real = new List<(string Name, List<KeyValuePair<string, string>> Values)>();
        if (metrica != null)
        {
            foreach (var g in games.Count > 0 ? games : ["1", "2"])
            {
                var d = MatchLoader.Metrica(metrica, Options.ParseInt(g, "Games"));
                real.Add((d.Name, MatchMeasures.Run(d)));
            }
            if (save)
            {
                referencePath ??= FindReference() ?? throw new ToolException("real-matches.csv not found: pass -Reference");
                using var w = new StreamWriter(referencePath);
                w.WriteLine("measure;" + string.Join(";", real.Select(r => r.Name)));
                foreach (var k in real[0].Values.Select(kv => kv.Key))
                    w.WriteLine(k + ";" + string.Join(";", real.Select(r => Value(r.Values, k))));
                Console.WriteLine($"reference -> {referencePath}");
            }
        }
        else if ((referencePath ?? FindReference()) is { } file && File.Exists(file))
        {
            var lines = File.ReadAllLines(file).Select(l => l.Split(';')).Where(c => c.Length > 1).ToList();
            for (int c = 1; c < lines[0].Length; c++)
                real.Add((lines[0][c], lines.Skip(1).Select(l => new KeyValuePair<string, string>(l[0], c < l.Length ? l[c] : "")).ToList()));
        }
        else Console.Error.WriteLine("no real-match reference (research/real-matches.csv): pass -Metrica or -Reference");
        columns.AddRange(real);
        keys ??= columns[0].Values.Select(kv => kv.Key).ToList();

        Console.WriteLine($"{"",-58}" + string.Join("", columns.Select(c => $"{Short(c.Name),16}")));
        foreach (var k in keys)
            Console.WriteLine(k.StartsWith('#') ? "\n" + k[1..] : $"{k,-58}" + string.Join("", columns.Select(c => $"{Value(c.Values, k),16}")));
        return 0;
    }

    static string Value(List<KeyValuePair<string, string>> values, string key) => values.FirstOrDefault(kv => kv.Key == key).Value ?? "-";
    static string Short(string name) => name.Length <= 15 ? name : name[..15];

    static string? FindReference()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                foreach (var f in new[] { Path.Combine(d.FullName, "real-matches.csv"), Path.Combine(d.FullName, "research", "real-matches.csv") })
                    if (File.Exists(f)) return f;
        return null;
    }
}

/// <summary>One sample of a match, in metres (x along the pitch, z across, centre 0) and seconds of match time.</summary>
sealed class MatchFrame
{
    public double T, Bx, Bz, Height;
    /// <summary>Index of the player on the ball, or -1.</summary>
    public int Carrier = -1;
    /// <summary>The keepers of side 0 and 1, and +1 when side 0 attacks towards +x.</summary>
    public int Keeper0, Keeper1, Dir0 = 1;
    public bool SetPiece;
    public double[] X = [], Z = [];   // NaN: not on the pitch
}

sealed class MatchData
{
    public string Name = "";
    public List<MatchFrame> Frames = [];
    public int[] Side = [];
    public double Dt, HalfLength;
    /// <summary>The arrival of each cross: frame, side, crosser, where the ball is.</summary>
    public List<(int Frame, int Side, int Crosser, double X, double Z)> Crosses = [];
    /// <summary>Pass completion from the match's own events, when it has them.</summary>
    public string EventPassCompletion = "";
}

static class MatchLoader
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const double Units = 233;   // game units per metre

    /// <summary>A positions log, one match per phase of its patch column (or one in all). Match time = real time x speed.</summary>
    public static List<MatchData> Pes(string path, int speed)
    {
        if (!File.Exists(path)) throw new ToolException($"{path}: not found");
        using var r = new StreamReader(path);
        var h = (r.ReadLine() ?? "").Split(',');
        int C(string n) => Array.IndexOf(h, n) is var i and >= 0 ? i : throw new ToolException($"{path}: no column '{n}' (log it again with pes6r positions)");
        int cMs = C("ms"), cCar = C("carrier"), cBx = C("bx"), cBz = C("bz"), cId = C("id"), cSt = C("state"), cPx = C("px"), cPz = C("pz"), cBy = C("by");
        int cPatch = Array.IndexOf(h, "patch");
        var byPhase = new SortedDictionary<string, MatchData>();
        var group = new List<string[]>();
        void Flush()
        {
            var rows = group.ToList(); group.Clear();
            if (rows.Count != 22 || rows.Any(g => g.Length < h.Length)) return;
            var f = new MatchFrame
            {
                T = long.Parse(rows[0][cMs], Inv) * speed / 1000.0, Bx = double.Parse(rows[0][cBx], Inv) / Units, Bz = double.Parse(rows[0][cBz], Inv) / Units,
                Height = -double.Parse(rows[0][cBy], Inv) / Units, X = new double[22], Z = new double[22], Keeper0 = 0, Keeper1 = 11,
            };
            var states = new int[22];
            foreach (var g in rows)
            {
                var id = int.Parse(g[cId], Inv) - 1;
                if (id is < 0 or > 21) return;
                f.X[id] = double.Parse(g[cPx], Inv) / Units; f.Z[id] = double.Parse(g[cPz], Inv) / Units; states[id] = int.Parse(g[cSt], Inv);
            }
            var carrier = int.Parse(rows[0][cCar], Inv);
            f.Carrier = carrier is >= 1 and <= 22 ? carrier - 1 : -1;
            f.SetPiece = f.Carrier >= 0 && states[f.Carrier] is 47 or 48;
            f.Dir0 = -Math.Sign(f.X[0]);
            // a match: both keepers deep in opposite halves, and not the frozen summary of a half's end (state 253)
            if (Math.Abs(f.X[0]) < 6000 / Units || Math.Abs(f.X[11]) < 6000 / Units || Math.Sign(f.X[0]) == Math.Sign(f.X[11]) || states.Contains(253)) return;
            var phase = cPatch >= 0 ? rows[0][cPatch] : "";
            if (!byPhase.TryGetValue(phase, out var d))
                byPhase[phase] = d = new MatchData
                {
                    Name = Path.GetFileNameWithoutExtension(path) + (phase != "" ? " " + phase : ""),
                    Side = [.. Enumerable.Range(0, 22).Select(i => i < 11 ? 0 : 1)], HalfLength = 12400 / Units,
                };
            d.Frames.Add(f);
        }
        while (r.ReadLine() is { } line)
        {
            var c = line.Split(',');
            if (group.Count > 0 && group[0][cMs] != c[cMs]) Flush();
            group.Add(c);
        }
        Flush();
        foreach (var d in byPhase.Values)
        {
            if (d.Frames.Count < 20) throw new ToolException($"{path}: no match in it ({d.Frames.Count} samples)");
            var gaps = d.Frames.Zip(d.Frames.Skip(1), (a, b) => b.T - a.T).Order().ToList();
            d.Dt = gaps[gaps.Count / 2];
            FindCrosses(d);
        }
        return [.. byPhase.Values];
    }

    /// <summary>
    /// Crosses of a log: a carrier wide in the last 19 m (not a set piece) lets the ball go and it rises above 0.65 m within 1 s;
    /// the arrival is its first return under 0.25 m.
    /// </summary>
    static void FindCrosses(MatchData d)
    {
        var F = d.Frames;
        int w = Math.Max(1, (int)Math.Round(1.0 / d.Dt));
        for (int k = 1; k < F.Count - 6 * w; k++)
        {
            var a = F[k - 1];
            if (a.Carrier < 0 || a.SetPiece || F[k].Carrier == a.Carrier || F[k].T - a.T > 1) continue;
            int side = d.Side[a.Carrier], dir = side == 0 ? a.Dir0 : -a.Dir0;
            if (a.X[a.Carrier] * dir < d.HalfLength - 19.3 || Math.Abs(a.Z[a.Carrier]) < 15) continue;
            int rise = Enumerable.Range(k, w).FirstOrDefault(j => F[j].Height > 0.65, -1);
            if (rise < 0) continue;
            int land = Enumerable.Range(rise, 6 * w).FirstOrDefault(j => F[j].Height < 0.25, k + 3 * w);
            if (F[land].T - a.T > 10) continue;
            d.Crosses.Add((land, side, a.Carrier, F[land].Bx, F[land].Bz));
            k += 4 * w;
        }
    }

    /// <summary>A match of the Metrica Sports sample data (games 1 and 2: the CSV format), one frame in two (12.5 Hz).</summary>
    public static MatchData Metrica(string folder, int game)
    {
        const int Step = 2; const double Length = 105, Width = 68;
        string Name(string n)
        {
            var f = Path.Combine(folder, $"Sample_Game_{game}_{n}.csv");
            return File.Exists(f) ? f : throw new ToolException($"{f}: not found (the Metrica sample data, github.com/metrica-sports/sample-data)");
        }
        var d = new MatchData { Name = $"Real match {game}", Dt = 0.04 * Step, HalfLength = Length / 2 };
        var names = new List<string>(); var side = new List<int>();
        var rows = new List<string[]>[2];
        for (int t = 0; t < 2; t++)
        {
            var lines = File.ReadAllLines(Name(t == 0 ? "RawTrackingData_Home_Team" : "RawTrackingData_Away_Team"));
            var header = lines[2].Split(',');
            for (int c = 3; c < header.Length - 2; c += 2) { names.Add(header[c]); side.Add(t); }
            rows[t] = [.. lines.Skip(3).Select(l => l.Split(','))];
        }
        d.Side = [.. side];
        int n = names.Count, nHome = side.Count(t => t == 0), count = Math.Min(rows[0].Count, rows[1].Count);
        static double P(string s) => s is "NaN" or "" ? double.NaN : double.Parse(s, Inv);
        int period = 0, k0 = 0, k1 = 0, d0 = 1;
        var frameIndex = new Dictionary<int, int>();
        for (int r = 0; r < count; r++)
        {
            if (r % Step != 0) continue;
            string[] h = rows[0][r], a = rows[1][r];
            var f = new MatchFrame { T = double.Parse(h[2], Inv), X = new double[n], Z = new double[n] };
            for (int i = 0; i < nHome; i++) { f.X[i] = (P(h[3 + 2 * i]) - 0.5) * Length; f.Z[i] = (P(h[4 + 2 * i]) - 0.5) * Width; }
            for (int i = nHome; i < n; i++) { f.X[i] = (P(a[3 + 2 * (i - nHome)]) - 0.5) * Length; f.Z[i] = (P(a[4 + 2 * (i - nHome)]) - 0.5) * Width; }
            f.Bx = (P(h[^2]) - 0.5) * Length; f.Bz = (P(h[^1]) - 0.5) * Width;
            int per = int.Parse(h[0], Inv);
            if (per != period)
            {
                // the kick-off: each side in its own half, and its keeper is its deepest player
                period = per;
                var home = Enumerable.Range(0, nHome).Where(i => !double.IsNaN(f.X[i])).ToList();
                var away = Enumerable.Range(nHome, n - nHome).Where(i => !double.IsNaN(f.X[i])).ToList();
                d0 = home.Average(i => f.X[i]) < 0 ? 1 : -1;
                k0 = home.MinBy(i => f.X[i] * d0); k1 = away.MaxBy(i => f.X[i] * d0);
            }
            f.Keeper0 = k0; f.Keeper1 = k1; f.Dir0 = d0;
            frameIndex[int.Parse(h[1], Inv)] = d.Frames.Count;
            d.Frames.Add(f);
        }
        int Index(int frame) { while (frame > 0 && !frameIndex.ContainsKey(frame)) frame--; return frameIndex.GetValueOrDefault(frame, 0); }

        // the player on the ball, from the events: from the moment he gets it (a pass received, a recovery) to his next pass, shot
        // or loss; a set piece gives nobody the ball (the wait before it is not play)
        int gainPlayer = -1, gainFrame = 0, passes = 0, lostPasses = 0; bool afterSetPiece = false;
        foreach (var e in File.ReadAllLines(Name("RawEventsData")).Skip(1).Select(l => l.Split(',')))
        {
            string type = e[1], sub = e[2]; int start = int.Parse(e[4], Inv), end = int.Parse(e[6], Inv);
            int from = names.IndexOf(e[8]), to = names.IndexOf(e[9]);
            switch (type)
            {
                case "PASS" or "SHOT" or "BALL LOST":
                    if (from >= 0 && from == gainPlayer && start >= gainFrame && start - gainFrame < 25 * 15)
                        for (int j = Index(gainFrame); j <= Index(start); j++) d.Frames[j].Carrier = from;
                    bool open = !afterSetPiece && !sub.Contains("GOAL KICK");
                    if (type == "PASS" && open) passes++;
                    if (type == "BALL LOST" && open && !sub.Contains("THEFT")) lostPasses++;
                    if (sub.Contains("CROSS") && from >= 0 && type != "SHOT" && e[12] != "NaN")
                        d.Crosses.Add((Index(end), d.Side[from], from, (P(e[12]) - 0.5) * Length, (P(e[13]) - 0.5) * Width));
                    if (type == "PASS" && to >= 0) { gainPlayer = to; gainFrame = end; } else gainPlayer = -1;
                    afterSetPiece = false;
                    break;
                case "RECOVERY": gainPlayer = from; gainFrame = start; afterSetPiece = false; break;
                case "SET PIECE": gainPlayer = -1; afterSetPiece = true; break;
                case "BALL OUT": gainPlayer = -1; break;
            }
        }
        if (passes + lostPasses > 0) d.EventPassCompletion = $"{100.0 * passes / (passes + lostPasses):F0}%";
        return d;
    }
}

static class MatchMeasures
{
    /// <summary>The measures of a match, in order; a key starting with # is a section title.</summary>
    public static List<KeyValuePair<string, string>> Run(MatchData d)
    {
        var o = new List<KeyValuePair<string, string>>();
        void Add(string k, string v) => o.Add(new(k, v));
        var F = d.Frames; int N = F.Count;
        int W(double s) => Math.Max(1, (int)Math.Round(s / d.Dt));
        int Dir(MatchFrame f, int side) => side == 0 ? f.Dir0 : -f.Dir0;
        bool Outfield(MatchFrame f, int i) => i != f.Keeper0 && i != f.Keeper1 && !double.IsNaN(f.X[i]);
        static double Dist(double x0, double z0, double x1, double z1) => Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
        // velocity (m/s) over the next half second; null across a hole in the log
        (double X, double Z)? Vel(int k, int i)
        {
            int w = W(0.5); if (k < 0 || k + w >= N) return null;
            double dt = F[k + w].T - F[k].T; if (dt <= 0 || dt > 1.5 * w * d.Dt) return null;
            if (double.IsNaN(F[k].X[i]) || double.IsNaN(F[k + w].X[i])) return null;
            return ((F[k + w].X[i] - F[k].X[i]) / dt, (F[k + w].Z[i] - F[k].Z[i]) / dt);
        }
        static string Pct(double a, double b) => b > 0 ? $"{100 * a / b:F0}%" : "-";
        static string Per(double a, double b, string f = "F2") => b > 0 ? (a / b).ToString(f, CultureInfo.InvariantCulture) : "-";
        static string Med(List<double> v, string f = "F1") => v.Count == 0 ? "-" : v.Order().ElementAt(v.Count / 2).ToString(f, CultureInfo.InvariantCulture);
        static string Mean(List<double> v) => v.Count == 0 ? "-" : $"{v.Average():F0}";

        var play = Enumerable.Range(0, N).Where(k => F[k].Carrier >= 0 && !F[k].SetPiece).ToList();
        double minutes = play.Count * d.Dt / 60;
        Add("#Sample", "");
        Add("match time logged (min)", $"{N * d.Dt / 60:F0}");
        Add("time with a player on the ball (min)", $"{minutes:F1}");

        double m5 = 0, m10 = 0, o5 = 0, o10 = 0, nOwn = 0, nOpp = 0;
        var bandsOwn = new double[5]; var bandsOpp = new double[5];
        var length = new List<double>[2] { [], [] }; var width = new List<double>[2] { [], [] };
        var speeds = new List<double>(); int turnedBack = 0, runs = 0;
        double support = 0, near15 = 0, ahead = 0, forward = 0, back = 0, still = 0, mates = 0, behind = 0, coming = 0;
        foreach (var k in play)
        {
            var f = F[k]; int c = f.Carrier, side = d.Side[c], dir = Dir(f, side);
            bool oppHalf = f.X[c] * dir > 0; if (oppHalf) support++;
            var xs = new List<double>[2] { [], [] }; var zs = new List<double>[2] { [], [] };
            for (int i = 0; i < d.Side.Length; i++)
            {
                if (!Outfield(f, i)) continue;
                bool mate = d.Side[i] == side;
                xs[mate ? 0 : 1].Add(f.X[i]); zs[mate ? 0 : 1].Add(f.Z[i]);
                if (i == c) continue;
                var dd = Dist(f.X[i], f.Z[i], f.X[c], f.Z[c]);
                if (dd < 10) { if (mate) m10++; else o10++; }
                if (dd < 5) { if (mate) m5++; else o5++; }
                var v = Vel(k, i);
                if (v is { } vv)
                {
                    var sp = Math.Sqrt(vv.X * vv.X + vv.Z * vv.Z);
                    speeds.Add(sp);
                    int b = sp < 1 ? 0 : sp < 2 ? 1 : sp < 4 ? 2 : sp < 5.5 ? 3 : 4;
                    if (mate) { bandsOwn[b]++; nOwn++; } else { bandsOpp[b]++; nOpp++; }
                    if (sp > 2.5 && Vel(k - W(1), i) is { } old && Math.Sqrt(old.X * old.X + old.Z * old.Z) is > 2.5 and var oldSp)
                    {
                        runs++;
                        if ((vv.X * old.X + vv.Z * old.Z) / (sp * oldSp) < -0.5) turnedBack++;
                    }
                }
                if (!oppHalf || !mate) continue;
                if (dd < 15) near15++;
                bool isAhead = f.X[i] * dir > f.X[c] * dir; if (isAhead) ahead++;
                if (v is not { } m) continue;
                mates++;
                double along = m.X * dir;
                if (along > 1) forward++; else if (along < -1) back++; else if (Math.Abs(m.Z) < 1) still++;
                if (isAhead && along > 4) behind++;
                // offering himself: closing on the player on the ball at more than 2 m/s, from less than 30 m
                var later = F[k + W(0.5)];
                if (dd < 30 && (dd - Dist(later.X[i], later.Z[i], f.X[c], f.Z[c])) / (later.T - f.T) > 2) coming++;
            }
            for (int t = 0; t < 2; t++)
                if (xs[t].Count >= 8) { length[t].Add(xs[t].Max() - xs[t].Min()); width[t].Add(zs[t].Max() - zs[t].Min()); }
        }
        Add("#Attack support (player on the ball in the opponents' half)", "");
        Add("team-mates within 15 m of him", Per(near15, support));
        Add("team-mates ahead of the ball", Per(ahead, support));
        Add("team-mates moving forward (> 1 m/s)", Pct(forward, mates));
        Add("team-mates moving back (> 1 m/s)", Pct(back, mates));
        Add("team-mates standing (< 1 m/s)", Pct(still, mates));
        Add("runs in behind at once (ahead, forward > 4 m/s)", Per(behind, support));
        Add("team-mates coming to the ball at once (> 2 m/s)", Per(coming, support));

        Add("#Around the player on the ball (whole pitch)", "");
        Add("team-mates within 5 m", Per(m5, play.Count));
        Add("opponents within 5 m", Per(o5, play.Count));
        Add("team-mates within 10 m", Per(m10, play.Count));
        Add("opponents within 10 m", Per(o10, play.Count));

        string[] bands = ["standing (< 1 m/s)", "walking (1-2 m/s)", "jogging (2-4 m/s)", "running (4-5.5 m/s)", "fast (> 5.5 m/s)"];
        Add("#Off-ball speed, team with the ball", "");
        for (int b = 0; b < 5; b++) Add("with: " + bands[b], Pct(bandsOwn[b], nOwn));
        Add("#Off-ball speed, team without the ball", "");
        for (int b = 0; b < 5; b++) Add("without: " + bands[b], Pct(bandsOpp[b], nOpp));
        speeds.Sort();
        Add("#Off-ball speed, everyone (m/s)", "");
        Add("median / 95 % / 99 %", speeds.Count == 0 ? "-" : string.Create(CultureInfo.InvariantCulture,
            $"{speeds[speeds.Count / 2]:F1}/{speeds[speeds.Count * 95 / 100]:F1}/{speeds[speeds.Count * 99 / 100]:F1}"));
        Add("#Chaos", "");
        Add("runs (> 2.5 m/s) turned back > 120 deg within 1 s", Pct(turnedBack, runs));

        Add("#Team shape (outfield players, m)", "");
        Add("length, team with the ball", Mean(length[0]));
        Add("width, team with the ball", Mean(width[0]));
        Add("length, team without the ball", Mean(length[1]));
        Add("width, team without the ball", Mean(width[1]));

        // releases (the ball leaves a player), challenges, time on the ball
        int kept = 0, lost = 0, won = 0, gaining = 0;
        var reach = new List<double>(); var passLength = new List<double>(); var onBall = new List<double>();
        double since = -1;
        for (int k = 0; k + 1 < N; k++)
        {
            var f = F[k]; int c = f.Carrier;
            if (c < 0) { since = -1; continue; }
            if (since < 0) since = f.T;
            if (F[k + 1].Carrier == c) continue;
            if (f.T > since) onBall.Add(f.T - since);
            since = -1;
            if (f.SetPiece) continue;
            int n = -1;
            for (int j = k + 1; j < N && F[j].T - f.T < 6.4; j++) if (F[j].Carrier >= 0 && F[j].Carrier != c) { n = j; break; }
            if (n < 0) continue;
            int t = F[n].Carrier; bool same = d.Side[t] == d.Side[c];
            if (double.IsNaN(f.X[t])) continue;
            // an opponent who stood within 3 m has it: a challenge, not a release
            if (!same && Dist(f.X[t], f.Z[t], f.X[c], f.Z[c]) < 3) { if (F[n].T - f.T < 1.2) won++; continue; }
            if (!same) { lost++; continue; }
            kept++; reach.Add(F[n].T - f.T); passLength.Add(Dist(f.X[c], f.Z[c], F[n].X[t], F[n].Z[t]));
            if ((F[n].X[t] - f.X[c]) * Dir(f, d.Side[c]) > 5) gaining++;
        }
        Add("#Passing (a release: the ball leaves a player, not by a challenge)", "");
        Add("releases per minute of possession", Per(kept + lost, minutes, "F1"));
        Add("a team-mate has the ball next", Pct(kept, kept + lost));
        Add("pass completion in the match's own events", d.EventPassCompletion == "" ? "-" : d.EventPassCompletion);
        Add("median pass length (m)", Med(passLength, "F0"));
        Add("passes gaining more than 5 m", Pct(gaining, kept));
        Add("median time until the team-mate has it (s)", Med(reach));
        Add("median time a player keeps the ball (s)", Med(onBall));
        Add("balls won by a challenge per minute of possession", Per(won, minutes, "F1"));

        // reactions: after the ball changes side, the time until a player's run turns by more than 60 degrees
        var delays = new List<double>[2] { [], [] }; var eligible = new int[2];
        for (int k = W(0.6); k + 1 < N; k++)
        {
            if (F[k].Carrier < 0) continue;
            int p = -1;
            for (int j = k - 1; j >= 0 && F[k].T - F[j].T < 6; j--) if (F[j].Carrier >= 0) { p = j; break; }
            if (p < 0 || d.Side[F[p].Carrier] == d.Side[F[k].Carrier]) continue;
            int winner = d.Side[F[k].Carrier];
            for (int i = 0; i < d.Side.Length; i++)
            {
                if (!Outfield(F[k], i)) continue;
                if (Vel(k - W(0.6), i) is not { } b || Math.Abs(b.X) + Math.Abs(b.Z) < 1.3) continue;
                int g = d.Side[i] == winner ? 1 : 0; eligible[g]++;
                for (int t = k; t < k + W(4); t++)
                {
                    if (Vel(t, i) is not { } a) break;
                    if (Math.Abs(a.X) + Math.Abs(a.Z) < 1.3) continue;
                    var cos = (a.X * b.X + a.Z * b.Z) / (Math.Sqrt(a.X * a.X + a.Z * a.Z) * Math.Sqrt(b.X * b.X + b.Z * b.Z));
                    if (cos < 0.5) { delays[g].Add(F[t].T - F[k].T); break; }
                }
            }
            k += W(4);
        }
        Add("#Reactions when the ball changes side (a run turning by > 60 deg)", "");
        Add("side that lost it: median delay (s)", Med(delays[0], "F2"));
        Add("side that lost it: runs turned within 4 s", Pct(delays[0].Count, eligible[0]));
        Add("side that won it: median delay (s)", Med(delays[1], "F2"));
        Add("side that won it: runs turned within 4 s", Pct(delays[1].Count, eligible[1]));

        var closest = new List<double>(); double inBox = 0; int nobody = 0;
        foreach (var c in d.Crosses)
        {
            var f = F[c.Frame]; int dir = Dir(f, c.Side);
            var ms = Enumerable.Range(0, d.Side.Length).Where(i => d.Side[i] == c.Side && Outfield(f, i) && i != c.Crosser).ToList();
            if (ms.Count == 0) continue;
            var near = ms.Min(i => Dist(f.X[i], f.Z[i], c.X, c.Z));
            closest.Add(near); if (near > 6) nobody++;
            inBox += ms.Count(i => f.X[i] * dir > d.HalfLength - 16.5 && Math.Abs(f.Z[i]) < 20.2);
        }
        Add("#Crosses (when the ball arrives)", "");
        Add("crosses found", $"{closest.Count}");
        Add("team-mates in the box", Per(inBox, closest.Count, "F1"));
        Add("median distance of the closest team-mate (m)", Med(closest));
        Add("no team-mate within 6 m", Pct(nobody, closest.Count));
        return o;
    }
}
