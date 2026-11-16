using Pes6Research;

namespace Pes6Research.Tests;

public class CompareTests
{
    /// <summary>
    /// A positions log where nobody moves: id 5 has the ball 10 m into the opponents' half, id 6 stands 5 m ahead of him and
    /// id 7 10 m to his side; everyone else is more than 20 m away.
    /// </summary>
    static string Log(int samples, Func<int, string>? phase = null)
    {
        var w = new StringWriter();
        w.WriteLine("ms,carrier,bx,bz,id,team,slot,state,px,pz,tx,tz,speed,role,mode,facing,run,heading,by" + (phase != null ? ",patch" : ""));
        for (var s = 0; s < samples; s++)
            for (var id = 1; id <= 22; id++)
            {
                var (x, z) = id switch
                {
                    1 => (-11800, 0), 12 => (11800, 0),
                    5 => (2330, 0), 6 => (3495, 0), 7 => (2330, 2330),
                    < 12 => (-6000, id * 500),
                    _ => (8000, (id - 12) * 400),
                };
                w.WriteLine($"{s * 50},5,2330,0,{id},{(id <= 11 ? 0 : 1)},{(id - 1) % 11},7,{x},{z},0,0,0,0,8,0,0,0,-23" + (phase != null ? "," + phase(s) : ""));
            }
        return w.ToString();
    }

    static List<MatchData> Load(string csv)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pes6r-compare-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, csv);
        try { return MatchLoader.Pes(path, speed: 2); }
        finally { File.Delete(path); }
    }

    static string Value(MatchData d, string key) => MatchMeasures.Run(d).First(kv => kv.Key == key).Value;

    [Fact]
    public void A_log_is_read_in_metres_and_match_seconds()
    {
        var d = Assert.Single(Load(Log(40)));
        Assert.Equal(40, d.Frames.Count);
        Assert.Equal(0.1, d.Dt, 3);             // 50 ms at x2
        Assert.Equal(10, d.Frames[0].X[4], 3);  // 2330 units
        Assert.Equal(4, d.Frames[0].Carrier);
        Assert.Equal(1, d.Frames[0].Dir0);      // the first keeper stands at -x
    }

    [Fact]
    public void Support_counts_the_team_mates_near_and_ahead_of_the_player_on_the_ball()
    {
        var d = Load(Log(40))[0];
        Assert.Equal("2.00", Value(d, "team-mates within 15 m of him"));
        Assert.Equal("1.00", Value(d, "team-mates ahead of the ball"));
        Assert.Equal("100%", Value(d, "team-mates standing (< 1 m/s)"));
        Assert.Equal("1.00", Value(d, "team-mates within 10 m"));   // id 6; id 7 is at 10 m exactly
        Assert.Equal("0.00", Value(d, "opponents within 10 m"));
        Assert.Equal("100%", Value(d, "with: standing (< 1 m/s)"));
    }

    [Fact]
    public void A_log_with_the_patch_column_gives_one_match_per_phase()
    {
        var both = Load(Log(80, s => s < 40 ? "A" : "B"));
        Assert.Equal(["A", "B"], both.Select(d => d.Name[^1..]));
        Assert.All(both, d => Assert.Equal(40, d.Frames.Count));
    }

    [Fact]
    public void A_log_without_a_match_is_refused()
    {
        Assert.Throws<ToolException>(() => Load(Log(3)));
    }
}
