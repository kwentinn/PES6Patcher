using System.Diagnostics;
using System.Runtime.InteropServices;
using static Pes6Research.GameLayout;

namespace Pes6Research;

/// <summary>
/// The 22 players and the ball every ~100 ms, one CSV line per sample and player (was pes6positions.ps1). Read-only, safe during
/// a whole match; feed the file to <c>pes6r depth</c>. Columns: ms, carrier, ball x/z, then per player: id (1-11 and 12-22 are the two
/// teams), team, slot (0 = keeper), state, x, z, target x/z, speed.
/// Lines are written as they come, so Ctrl+C or the game closing (it crashes by itself now and then) keeps what was sampled.
/// </summary>
static class PositionsCommand
{
    public const string Usage = "positions [-Seconds 60] [-Out positions.csv] [-Every 100] [-StartDelay 5]\n" +
                                "                                                 CSV of the 22 players and the ball (beeps when StartDelay is given)";

    public static int Run(Options o, string processName)
    {
        var seconds = o.Int("Seconds", 60);
        var every = o.Int("Every", 100);
        var startDelay = o.Int("StartDelay", 0);
        var outPath = Path.GetFullPath(o.String("Out") ?? "positions.csv");
        o.CheckAllUsed();

        using var game = GameProcess.Open(processName);
        var stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };
        timeBeginPeriod(1); // Sleep to the millisecond, not to the 15.6 ms tick
        try
        {
            if (startDelay > 0) { Thread.Sleep(startDelay * 1000); Beep(1200, 300); }
            using var w = new StreamWriter(outPath) { AutoFlush = false };
            w.WriteLine("ms,carrier,bx,bz,id,team,slot,state,px,pz,tx,tz,speed");
            var carrier = new byte[1];
            var all = new byte[PlayerCount * PlayerSize];
            var samples = 0;
            var sw = Stopwatch.StartNew();
            while (!stop && sw.Elapsed.TotalSeconds < seconds)
            {
                var ms = (int)sw.ElapsedMilliseconds;
                // No line without the players (a sample of zeros would bias pes6depth); the carrier and the ball read as 0 when
                // unreadable, as the ball does before a match (null pointer).
                if (!game.TryRead(Players, all))
                {
                    if (game.HasExited) { Console.Error.WriteLine("the game closed"); break; }
                    Sleep(every, sw, ms); continue;
                }
                if (!game.TryRead(Carrier, carrier)) carrier[0] = 0;
                var (bx, _, bz) = Ball(game);
                for (var i = 0; i < PlayerCount; i++)
                {
                    var p = i * PlayerSize;
                    w.WriteLine($"{ms},{carrier[0]},{bx:F0},{bz:F0},{i + 1},{all[p + Team]},{all[p + Slot]},{all[p + State]}," +
                                $"{BitConverter.ToSingle(all, p + X):F0},{BitConverter.ToSingle(all, p + Z):F0}," +
                                $"{BitConverter.ToSingle(all, p + TargetX):F0},{BitConverter.ToSingle(all, p + TargetZ):F0}," +
                                $"{BitConverter.ToInt32(all, p + Speed)}");
                }
                if (++samples % 10 == 0) w.Flush();
                Sleep(every, sw, ms);
            }
            w.Flush();
            if (startDelay > 0) Beep(600, 700);
            Console.WriteLine($"{samples} samples -> {outPath}");
        }
        finally { timeEndPeriod(1); }
        return 0;
    }

    static void Sleep(int every, Stopwatch sw, int start)
    {
        var left = every - ((int)sw.ElapsedMilliseconds - start);
        if (left > 0) Thread.Sleep(left);
    }

    [DllImport("kernel32.dll")] static extern bool Beep(int frequency, int ms);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);
}
