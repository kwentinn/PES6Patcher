using static Pes6Research.GameLayout;

namespace Pes6Research;

/// <summary>
/// Beep-guided measurement (was pes6heading.ps1): samples the stick angle and the heading (+0x2C) of the given player objects every
/// 100 ms, then summarises how many distinct headings each player took and whether they are 45-degree multiples.
/// </summary>
static class HeadingCommand
{
    public const string Usage = "heading -Players 0x3BDD700,0x3BDDB80 [-Seconds 10] [-StartDelay 5]\n" +
                                "                                                 players' real heading against the stick angle (45-degree steps or not)";

    public static int Run(Options o, string processName)
    {
        var players = o.List("Players").Select(p => (uint)Options.ParseInt(p, "Players")).ToList();
        var seconds = o.Int("Seconds", 10);
        var startDelay = o.Int("StartDelay", 5);
        o.CheckAllUsed();
        if (players.Count == 0) throw new ToolException("-Players is required (player object addresses)");

        using var game = GameProcess.Open(processName);
        using var run = new Sampling();
        run.Start(startDelay);
        var samples = new List<(ushort Stick, byte Magnitude, ushort[] Headings)>();
        while (run.Running(game, seconds))
        {
            var ms = run.Clock.ElapsedMilliseconds;
            var pad = game.ReadOrZero(Pad, 4);
            samples.Add((BitConverter.ToUInt16(pad, 0), pad[2],
                [.. players.Select(p => BitConverter.ToUInt16(game.ReadOrZero(p + (uint)Heading, 2)))]));
            run.Pace(100, ms);
        }
        Sampling.End();

        var moving = samples.Where(s => s.Magnitude > 0).ToList();
        Console.WriteLine($"{samples.Count} samples, {moving.Count} with the stick pushed");
        Console.WriteLine("stick: " + Top(moving.Select(s => Degrees(s.Stick, 1)), 6));
        for (var i = 0; i < players.Count; i++)
        {
            var values = moving.Select(s => s.Headings[i]).ToList();
            var off45 = values.Count(v => v % 0x2000 != 0);
            Console.WriteLine($"0x{players[i]:X}: {values.Distinct().Count()} different headings, {off45}/{values.Count} off multiples of 45°; " +
                              $"most frequent: {Top(values, 5, v => Degrees(v, 1))}");
        }
        return 0;
    }

    /// <summary>The most frequent values, "90° x12, 45° x3" (ties in order of first appearance), shown in degrees.</summary>
    static string Top<T>(IEnumerable<T> values, int count, Func<T, double>? degrees = null) where T : notnull =>
        string.Join(", ", values.GroupBy(v => v).OrderByDescending(g => g.Count()).Take(count)
            .Select(g => $"{(degrees == null ? g.Key : degrees(g.Key))}° x{g.Count()}"));
}
