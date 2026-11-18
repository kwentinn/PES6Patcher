using static Pes6Research.GameLayout;

namespace Pes6Research;

/// <summary>
/// Prototype in the running game (no patch): the designated-player byte of role 0x1C (0x3BCDC4B, read by FUN_006236e0 at 0x623704)
/// is empty about 60 % of the time. While it is empty and a player has the ball, this writes the index (the id, 1-22) of the carrier's most
/// advanced team-mate, so that player gets placement mode 0x1C (the attacker's forward position) instead of staying in mode 8.
/// </summary>
static class DesignateCommand
{
    public const string Usage = "designate [-Seconds 60] [-Always] [-Runner]                     prototype: while the 0x1C designation is empty, designate the most advanced team-mate of the carrier";
    const uint DesignatedByte = 0x3BCDC4B, RunnerByte = 0x3BCDCA9;   // the runner of mode 0x0F: [0x3BCDCA9 + side]

    public static int Run(Options o, string processName)
    {
        var seconds = o.Int("Seconds", 60);
        var always = o.Flag("Always");
        var runner = o.Flag("Runner");
        o.CheckAllUsed();
        using var game = GameProcess.Open(processName, write: true);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int written = 0;
        var chosen = new Dictionary<int, int>();
        while (clock.Elapsed.TotalSeconds < seconds && !game.HasExited)
        {
            Thread.Sleep(15);
            var carrier = game.ReadU8(Carrier);
            if (carrier is < 1 or > PlayerCount || game.ReadU8(RestartState) != 0) continue;
            var target = runner ? RunnerByte + game.ReadU8(Player(carrier) + 0x13) : DesignatedByte;
            if (game.ReadU8(target) != 0 && !always) continue;
            var team = game.ReadU8(Player(carrier) + Team);
            float X(int id) => BitConverter.ToSingle(game.ReadOrZero(Player(id) + GameLayout.X, 4));
            int Keeper(int t) => Enumerable.Range(1, PlayerCount).First(i => game.ReadU8(Player(i) + Team) == t && game.ReadU8(Player(i) + Slot) == 0);
            var own = Keeper(team);
            var other = Enumerable.Range(1, PlayerCount).Where(i => game.ReadU8(Player(i) + Team) != team).Select(i => game.ReadU8(Player(i) + Slot) == 0 ? i : 0).FirstOrDefault(i => i != 0);
            if (other == 0) continue;
            var dir = Math.Sign(X(other) - X(own));
            var best = Enumerable.Range(1, PlayerCount)
                .Where(i => i != carrier && game.ReadU8(Player(i) + Team) == team && game.ReadU8(Player(i) + Slot) != 0 && (!runner || (game.ReadU8(Player(i) + Role) >= 24 && game.ReadU8(Player(i) + PlacementMode) == 8)))
                .OrderByDescending(i => dir * X(i)).FirstOrDefault();
            if (best == 0) continue;
            game.Write(target, [(byte)best]);
            written++;
            chosen[best] = chosen.GetValueOrDefault(best) + 1;
        }
        Console.WriteLine($"{written} designations: " + string.Join(" ", chosen.OrderBy(c => c.Key).Select(c => $"id{c.Key}:{c.Value}")));
        return 0;
    }
}
