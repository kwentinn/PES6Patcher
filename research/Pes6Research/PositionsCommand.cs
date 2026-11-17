using static Pes6Research.GameLayout;

namespace Pes6Research;

/// <summary>
/// The 22 players and the ball every ~100 ms, one CSV line per sample and player (was pes6positions.ps1). Read-only, safe during
/// a whole match; feed the file to <c>pes6r depth</c>. Columns: ms, carrier, ball x/z, then per player: id (1-11 and 12-22 are the
/// two teams), team, slot (0 = keeper), state, x, z, target x/z, speed.
/// Lines are written as they come, so Ctrl+C or the game closing (it crashes by itself now and then) keeps what was sampled.
/// </summary>
static class PositionsCommand
{
    public const string Usage = "positions [-Seconds 60] [-Out positions.csv] [-Every 100] [-StartDelay 5] [-Patch name]\n" +
                                "                                                 CSV of the 22 players and the ball, with role, placement mode, angles and the restart state; -Patch adds whether that patch is on (A/B)";

    public static int Run(Options o, string processName)
    {
        var seconds = o.Int("Seconds", 60);
        var every = o.Int("Every", 100);
        var startDelay = o.Int("StartDelay", 0);
        var outPath = Path.GetFullPath(o.String("Out") ?? "positions.csv");
        var patchName = o.String("Patch");
        var patches = patchName != null ? PatchSet.Load(o.String("File")) : null;
        o.CheckAllUsed();
        if (patchName != null && !patches!.Contains(patchName)) throw new ToolException($"unknown patch '{patchName}'");

        using var game = GameProcess.Open(processName);
        using var run = new Sampling();
        run.Start(startDelay, beep: startDelay > 0);
        using var w = new StreamWriter(outPath);
        w.WriteLine("ms,carrier,bx,bz,id,team,slot,state,px,pz,tx,tz,speed,role,mode,facing,run,heading,by" + (patchName != null ? ",patch" : "") + ",restart");
        var all = new byte[PlayerCount * PlayerSize];
        var samples = 0;
        while (run.Running(game, seconds))
        {
            var ms = run.Clock.ElapsedMilliseconds;
            // No line without the players (a sample of zeros would bias depth); the carrier and the ball read as 0 when
            // unreadable, as the ball does before a match (null pointer).
            if (game.TryRead(Players, all))
            {
                var carrier = game.ReadOrZero(Carrier, 1)[0];
                var (bx, by, bz) = Ball(game);
                var restart = game.ReadOrZero(RestartState, 1)[0];   // 0 in play, 2 free kick, 6 throw-in, 7 goal / half-time ...
                // A: the patch is off, B: it is on (for an A/B run beside pes6r ab)
                var phase = patchName == null ? "" : patches!.State(game, patchName) == "applied" ? ",B" : ",A";
                for (var i = 0; i < PlayerCount; i++)
                {
                    var p = i * PlayerSize;
                    w.WriteLine($"{ms},{carrier},{bx:F0},{bz:F0},{i + 1},{all[p + Team]},{all[p + Slot]},{all[p + State]}," +
                                $"{BitConverter.ToSingle(all, p + X):F0},{BitConverter.ToSingle(all, p + Z):F0}," +
                                $"{BitConverter.ToSingle(all, p + TargetX):F0},{BitConverter.ToSingle(all, p + TargetZ):F0}," +
                                $"{BitConverter.ToInt32(all, p + Speed)},{all[p + Role]},{all[p + PlacementMode]}," +
                                $"{Degrees(BitConverter.ToUInt16(all, p + Facing))},{Degrees(BitConverter.ToUInt16(all, p + Move))}," +
                                $"{Degrees(BitConverter.ToUInt16(all, p + Heading))},{by:F0}{phase},{restart}");
                }
                if (++samples % 10 == 0) w.Flush();
            }
            run.Pace(every, ms);
        }
        w.Flush();
        if (startDelay > 0) Sampling.End();
        Console.WriteLine($"{samples} samples -> {outPath}");
        return 0;
    }
}
