using static Pes6Research.GameLayout;

namespace Pes6Research;

/// <summary>
/// Logs of the ball carrier while someone plays, guided by beeps (high: start, low: end). Lines are written as they come.
/// <list type="bullet">
/// <item><c>driftlog</c> (was pes6driftlog.ps1): every ~5 ms while the carrier moves: positions, the actual run direction (from
/// the position change), facing +0x26, move +0x28, heading +0x2C, stick, ball direction, speed, distance.</item>
/// <item><c>turnlog</c> (was pes6turnlog.ps1): one line each time a watched field changes (carrier, stick, state, timer,
/// animation, frame, +0x26, +0x28, heading +0x2C, asked +0x2E, speed, ball direction and speed). It showed the 22.5-degree steps
/// of the sprint turns.</item>
/// </list>
/// Angles in degrees.
/// </summary>
static class CarrierLogCommands
{
    public const string DriftUsage = "driftlog [-Seconds 30] [-Out drift.csv] [-StartDelay 3]\n" +
                                     "                                                 carrier and ball every ~5 ms while the carrier moves (run direction, distance)";
    public const string TurnUsage = "turnlog [-Seconds 30] [-Out turn.csv] [-StartDelay 5]\n" +
                                    "                                                 one line per change of the carrier's fields (state, animation, headings)";

    public static int Drift(Options o, string processName)
    {
        var seconds = o.Int("Seconds", 30);
        var outPath = Path.GetFullPath(o.String("Out") ?? "drift.csv");
        var startDelay = o.Int("StartDelay", 3);
        o.CheckAllUsed();

        using var game = GameProcess.Open(processName);
        using var run = new Sampling();
        using var w = new StreamWriter(outPath);
        w.WriteLine("ms,carrier,state,anim,stick,mag,f26,f28,h2C,runDir,speed,px,pz,bx,bz,ballDir,ballSpeed,dist");
        run.Start(startDelay);
        double lpx = 0, lpz = 0; // in double, as PowerShell computed: same run direction and distance
        var last = -1;
        var rows = 0;
        while (run.Running(game, seconds))
        {
            var c = game.ReadOrZero(Carrier, 1)[0];
            if (c is >= 1 and <= 22)
            {
                var pl = game.ReadOrZero(Player(c), 0xF0);
                var anim = game.ReadOrZero(BitConverter.ToUInt32(pl, AnimationPointer) + AnimationId, 2);
                var pad = game.ReadOrZero(Pad, 4);
                var ball = game.ReadOrZero(BallAddress(game) + BallPosition, 0x50);
                double px = BitConverter.ToSingle(pl, X), pz = BitConverter.ToSingle(pl, Z);
                double bx = BitConverter.ToSingle(ball, 0), bz = BitConverter.ToSingle(ball, 8);
                if (px != lpx || pz != lpz)
                {
                    var runDir = c == last ? $"{Math.Round(Math.Atan2(pz - lpz, px - lpx) * 180 / Math.PI)}" : "";
                    w.WriteLine($"{run.Clock.ElapsedMilliseconds},{c},{pl[State]},{BitConverter.ToUInt16(anim)},{Deg(pad, 0)},{pad[2]}," +
                                $"{Deg(pl, Facing)},{Deg(pl, Move)},{Deg(pl, Heading)},{runDir},{BitConverter.ToInt32(pl, Speed)}," +
                                $"{px:F0},{pz:F0},{bx:F0},{bz:F0},{Deg(ball, BallDirection - BallPosition)}," +
                                $"{BitConverter.ToInt32(ball, BallSpeed - BallPosition)},{Math.Sqrt((bx - px) * (bx - px) + (bz - pz) * (bz - pz)):F0}");
                    if (++rows % 50 == 0) w.Flush();
                }
                lpx = px; lpz = pz; last = c;
            }
            Thread.Sleep(5);
        }
        Sampling.End();
        Console.WriteLine($"{rows} rows -> {outPath}");
        return 0;
    }

    public static int Turn(Options o, string processName)
    {
        var seconds = o.Int("Seconds", 30);
        var outPath = Path.GetFullPath(o.String("Out") ?? "turn.csv");
        var startDelay = o.Int("StartDelay", 5);
        o.CheckAllUsed();

        using var game = GameProcess.Open(processName);
        using var run = new Sampling();
        using var w = new StreamWriter(outPath);
        w.WriteLine("ms,carrier,stick,mag,state,timer,anim,frame,f26,f28,head2C,ask2E,speed,ballDir,ballSpeed");
        run.Start(startDelay);
        var lastKey = "";
        var rows = 0;
        while (run.Running(game, seconds))
        {
            var c = game.ReadOrZero(Carrier, 1)[0];
            var pad = game.ReadOrZero(Pad, 4);
            var ball = game.ReadOrZero(BallAddress(game) + BallSpeed, 12);
            if (c is >= 1 and <= 22)
            {
                var pl = game.ReadOrZero(Player(c), 0x60);
                var anim = game.ReadOrZero(BitConverter.ToUInt32(pl, AnimationPointer) + AnimationId, 8);
                var key = $"{c},{Deg(pad, 0)},{pad[2]},{pl[State]},{BitConverter.ToUInt16(pl, GameLayout.Timer)},{BitConverter.ToUInt16(anim, 0)}," +
                          $"{BitConverter.ToUInt16(anim, AnimationFrame - AnimationId)},{Deg(pl, Facing)},{Deg(pl, Move)},{Deg(pl, Heading)}," +
                          $"{Deg(pl, Asked)},{BitConverter.ToInt32(pl, Speed)},{Deg(ball, BallDirection - BallSpeed)}";
                if (key != lastKey)
                {
                    w.WriteLine($"{run.Clock.ElapsedMilliseconds},{key},{BitConverter.ToInt32(ball, 0)}");
                    lastKey = key;
                    if (++rows % 50 == 0) w.Flush();
                }
            }
            Thread.Sleep(1); // the game changes once a frame (16 ms): no need to spin a core
        }
        Sampling.End();
        Console.WriteLine($"{rows} rows -> {outPath}");
        return 0;
    }

    static double Deg(byte[] b, int offset) => Degrees(BitConverter.ToUInt16(b, offset));
}
