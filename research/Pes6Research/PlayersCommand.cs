using static Pes6Research.GameLayout;

namespace Pes6Research;

/// <summary>One snapshot of the 22 players (id, team, slot, state, position, target, buttons) and the ball (was pes6players.ps1).</summary>
static class PlayersCommand
{
    public const string Usage = "players                                          snapshot of the 22 players and the ball: who carries it, where everybody is";

    public static int Run(Options o, string processName)
    {
        o.CheckAllUsed();
        using var game = GameProcess.Open(processName);
        var all = game.Read(Players, PlayerCount * PlayerSize);
        var carrier = game.ReadU8(Carrier);
        var (bx, by, bz) = Ball(game);
        var w = new StringWriter();
        w.WriteLine($"carrier={carrier} ball x={bx:F0} y={by:F0} z={bz:F0}");
        w.WriteLine("id team slot state  px     pz     t6C    t70   buttons");
        for (var i = 0; i < PlayerCount; i++)
        {
            var p = i * PlayerSize;
            w.WriteLine($"{i + 1,2} {all[p + Team],4} {all[p + Slot],4} {all[p + State],5} {BitConverter.ToSingle(all, p + X),6:F0} " +
                        $"{BitConverter.ToSingle(all, p + Z),6:F0} {BitConverter.ToInt32(all, p + TargetX),6} " +
                        $"{BitConverter.ToInt32(all, p + TargetZ),6} {BitConverter.ToInt32(all, p + Buttons):X8}");
        }
        Console.Write(w);
        return 0;
    }
}
