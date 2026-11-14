using Pes6Research;
using static Pes6Research.DepthCommand;

namespace Pes6Research.Tests;

public class AnalysisTests
{
    /// <summary>One sample of a positions CSV: team 0 attacks right (keeper at -11800), team 1 left, 10 outfielders each.</summary>
    static string Csv(int ms, int carrier, double bx, double[] team0, double[] team1)
    {
        var w = new StringWriter();
        var id = 1;
        void Team(int t, double keeper, double[] xs)
        {
            w.WriteLine($"{ms},{carrier},{bx},0,{id++},{t},0,1,{keeper},0,0,0,0");
            for (var i = 0; i < xs.Length; i++) w.WriteLine($"{ms},{carrier},{bx},0,{id++},{t},{i + 1},{(i == 0 ? 7 : 1)},{xs[i]},{i * 100},0,0,0");
        }
        Team(0, -11800, team0);
        Team(1, 11800, team1);
        return w.ToString();
    }

    const string Header = "ms,carrier,bx,bz,id,team,slot,state,px,pz,tx,tz,speed\n";
    static readonly double[] Team0 = [-8000, -7000, -6000, -5000, -2000, -1000, 0, 1000, 3000, 4000];
    static readonly double[] Team1 = [8000, 7000, 6000, 5000, 2000, 1000, 0, -1000, -3000, -4000];

    static List<Row> Rows(string csv) => ReadRows(new StringReader(Header + csv), "p", "test").Rows;

    [Fact]
    public void The_back_line_is_the_mean_of_the_4_deepest_seen_from_the_own_goal()
    {
        var samples = Compute(Rows(Csv(100, 3, 2000, Team0, Team1)));
        Assert.Equal(2, samples.Count);
        var (a, b) = (samples[0], samples[1]);
        // team 0: x + 11800; deepest four -8000..-5000 -> 3800..6800
        Assert.Equal(5300, a.Line);
        Assert.Equal((3800.0, 15800.0, 12000.0), (a.Deep, a.High, a.Length));
        Assert.Equal(900, a.Width); // z 0..900
        Assert.Equal(13800, a.Ball);
        Assert.Equal(1, a.St7);
        // team 1 mirrors it (x * -1 + 11800): same line, the ball seen from the other goal
        Assert.Equal(5300, b.Line);
        Assert.Equal(9800, b.Ball);
    }

    [Fact]
    public void Possession_comes_from_the_carrier_team()
    {
        var own = Compute(Rows(Csv(0, 3, 0, Team0, Team1)));    // id 3: team 0
        Assert.Equal(("own", "opp"), (own[0].Poss, own[1].Poss));
        var none = Compute(Rows(Csv(0, 0, 0, Team0, Team1)));
        Assert.Equal(("none", "none"), (none[0].Poss, none[1].Poss));
    }

    [Fact]
    public void A_team_with_fewer_than_10_outfielders_is_left_out()
    {
        var samples = Compute(Rows(Csv(0, 0, 0, Team0[..9], Team1)));
        Assert.Equal(1, Assert.Single(samples).Team);
    }

    [Fact]
    public void A_line_cut_short_by_a_stopped_run_is_left_out_and_counted()
    {
        var csv = Header + Csv(0, 0, 0, Team0, Team1) + "100,0,0,0,1,0,0";
        var (rows, skipped) = ReadRows(new StringReader(csv), "p", "test");
        Assert.Equal(22, rows.Count);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void The_targets_are_read_with_field_t_and_a_missing_column_is_reported()
    {
        Assert.Equal(22, ReadRows(new StringReader(Header + Csv(0, 0, 0, Team0, Team1)), "t", "test").Rows.Count);
        Assert.Throws<ToolException>(() => ReadRows(new StringReader("ms,carrier\n"), "p", "test"));
    }

    [Theory]
    [InlineData(-500, 0)]
    [InlineData(0, 0)]
    [InlineData(3934, 1)]
    [InlineData(23600, 5)]
    [InlineData(40000, 5)]
    public void Ball_zones_are_sixths_of_the_pitch(double ball, int zone) => Assert.Equal(zone, Zone(ball));

    [Fact]
    public void The_teams_csv_uses_a_decimal_point()
    {
        var w = new StringWriter();
        WriteCsv(w, [new Sample(5, 0, "opp", 1.5, 2.25, 0, 0, 0, 0, 1)]);
        Assert.Contains("\"5\",\"0\",\"opp\",\"1.5\",\"2.25\"", w.ToString());
    }

    /// <summary>The 22 players' bytes as the game holds them: index 0 and 11 are the keepers.</summary>
    static byte[] Players(float keeper0, (float X, float Z, byte Role)[] team0, (float X, float Z, byte Role)[] team1)
    {
        var all = new byte[22 * GameLayout.PlayerSize];
        void Put(int index, float x, float z, byte role)
        {
            var p = index * GameLayout.PlayerSize;
            BitConverter.GetBytes(x).CopyTo(all, p + GameLayout.X);
            BitConverter.GetBytes(z).CopyTo(all, p + GameLayout.Z);
            all[p + GameLayout.Role] = role;
        }
        Put(0, keeper0, 0, 0);
        Put(11, -keeper0, 0, 0);
        for (var i = 0; i < 10; i++) { Put(1 + i, team0[i].X, team0[i].Z, team0[i].Role); Put(12 + i, team1[i].X, team1[i].Z, team1[i].Role); }
        return all;
    }

    static readonly (float, float, byte)[] Shape =
        [(-6000, -3000, 1), (-6000, -1000, 2), (-6000, 1000, 3), (-6000, 3000, 4),   // defenders 1-7
         (-2000, -2000, 0x0A), (-2000, 0, 0x0B), (-2000, 2000, 0x0C), (-1000, 0, 0x10), // midfielders 8-0x17
         (2000, -500, 0x18), (2000, 500, 0x19)];                                       // forwards 0x18+

    [Fact]
    public void Shape_splits_the_lines_by_role_in_each_side_own_direction()
    {
        var mirrored = Shape.Select(p => (-p.Item1, p.Item2, p.Item3)).ToArray();
        var s = ShapeCommand.Take(Players(-12000, Shape, mirrored), carrier: 14, bx: 500).ToList();
        Assert.Equal(2, s.Count);
        var (a, b) = (s[0], s[1]);
        Assert.Equal(("opp", "own"), (a.Poss, b.Poss)); // carrier 14 is in team 1
        Assert.Equal((-6000.0, -1750.0, 2000.0), (a.Def, a.Mid, a.Fwd));
        Assert.Equal((a.Def, a.Mid, a.Fwd), (b.Def, b.Mid, b.Fwd)); // the same shape seen from the other goal
        Assert.Equal((500.0, -500.0), (a.Ball, b.Ball));
        Assert.Equal((8000.0, 6000.0), (a.Length, a.Width));
        Assert.Equal("1 2 3 4 A B C 10 18 19", a.Roles);
    }

    [Fact]
    public void Shape_skips_a_team_whose_keeper_is_not_there_yet()
    {
        var all = Players(-12000, Shape, Shape);
        BitConverter.GetBytes(0f).CopyTo(all, 11 * GameLayout.PlayerSize + GameLayout.X);
        Assert.Single(ShapeCommand.Take(all, carrier: 0, bx: 0));
    }

    [Fact]
    public void The_spread_is_the_population_standard_deviation()
    {
        Assert.Equal(0, ShapeCommand.Sd([5]));
        Assert.Equal(2, ShapeCommand.Sd([2, 4, 4, 4, 5, 5, 7, 9]));
    }
}
