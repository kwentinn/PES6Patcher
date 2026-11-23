using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using static Pes6Research.GameLayout;

namespace Pes6Research;

/// <summary>
/// <c>pes6r hud</c>: a debug overlay for the running game, drawn by a separate click-through window that only reads the game's memory
/// (no debugger, nothing written to the exe). Shows the live match state (restart state, carrier, pad, ball, placement lines, a mini map)
/// and one line per kick (shot or pass: type, buttons, gauge, receiver, target, ball speed). Every kick is also appended to a log
/// file so the user can send it back. Insert shows or hides the overlay, Delete clears the kick list.
/// </summary>
static class HudCommand
{
    public const string Usage = "hud [-Text] [-Calls] [-Guide] [-GuideRun] [-GuideCross] [-GuideCrossRun] [-GuideSteer] [-GuidePass] [-Seconds N] [-Log hud-events.log] [-Every 15]\n" +
                                "                                                 debug overlay over the game window (Insert = show/hide, Delete = clear); -Text prints the kicks in the console instead;\n" +
                                "                                                 -Guide shows on screen, with a beep at each step, what to do with the pad (right stick, R1) and logs the raw pad changes;\n" +
                                "                                                 -Calls attaches the debugger to the ball kick function 0x478020 and adds the Ctrl+K page: every call with its callers";

    const uint KickFunction = 0x478020;

    public static int Run(Options o, string processName)
    {
        var text = o.Flag("Text");
        var seconds = o.Int("Seconds", 0);
        var every = o.Int("Every", 15);
        var calls = o.Flag("Calls");
        var guideRun = o.Flag("GuideRun");
        var guideCross = o.Flag("GuideCross");
        var guideSteer = o.Flag("GuideSteer");
        var guideCrossRun = o.Flag("GuideCrossRun");
        guideCross |= guideCrossRun;
        var guidePass = o.Flag("GuidePass");
        var guide = o.Flag("Guide") || guideRun || guideCross || guideSteer || guidePass;
        var log = Path.GetFullPath(o.String("Log") ?? Path.Combine(AppContext.BaseDirectory, "hud-events.log"));
        o.CheckAllUsed();
        using var game = GameProcess.Open(processName);
        var tracker = new HudTracker();
        void Record(string line) { try { File.AppendAllText(log, line + Environment.NewLine); } catch (IOException) { } }
        tracker.KickLogged += line => { Record(line); if (text) Console.WriteLine(line); };
        Record($"# hud started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        if (!text) Console.WriteLine($"overlay on; kicks logged to {log}. Insert = show/hide, Delete = clear, Ctrl+C here to stop.");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var stop = new CancellationTokenSource();
        Thread? debugger = null;
        tracker.CallLogged += line => Record("call " + line);
        tracker.PadLogged += line => Record("pad " + line);
        tracker.RunLogged += line => Record("run " + line);
        tracker.RunLog = guideRun;
        tracker.PassLogged += line => Record("pass " + line);
        tracker.PassLog = guidePass;
        if (calls)
        {
            debugger = new Thread(() =>
            {
                try { HardwareBreakpoints.Run(game, game.Id, [KickFunction], HardwareBreakpoints.ExecuteDr7(1), execute: true, seconds > 0 ? seconds : 1e9,
                          hit => { if (hit.Eip == KickFunction) tracker.AddCall(game, hit.Esp, clock.ElapsedMilliseconds); }, () => stop.IsCancellationRequested); }
                catch (ToolException e) { Console.Error.WriteLine(e.Message); }
            }) { IsBackground = true };
            debugger.Start();
        }
        try
        {
            if (text)
            {
                while (!game.HasExited && (seconds == 0 || clock.Elapsed.TotalSeconds < seconds))
                {
                    tracker.Update(game, clock.ElapsedMilliseconds);
                    Thread.Sleep(every);
                }
                return 0;
            }
            Application.EnableVisualStyles();
            Application.Run(new HudForm(game, tracker, clock, every, seconds, guide, guideRun, guideCross, guideSteer, guideCrossRun, guidePass));
            return 0;
        }
        finally { stop.Cancel(); debugger?.Join(); }   // the debugger must detach cleanly, or the game crashes on the leftover breakpoint
    }
}

/// <summary>One kick being measured: the state change that starts it, then the ball's launch over the next 500 ms.</summary>
sealed class PendingKick
{
    public long StartMs; public int Player, State, Receiver, Genre, Gauge;
    public uint Buttons, Pressed;
    public float TargetX, TargetZ, StartBallX, StartBallZ, EndBallX, EndBallZ;
    public int PeakSpeed, VzAtPeak, Run;
    public float VzFloatAtPeak;
}

/// <summary>Reads the match state each tick and turns it into live lines and a kick history.</summary>
sealed class HudTracker
{
    public const uint PadMemory = 0x3BCEE40;           // pressed at +0, held at +8, 24 bytes per controller (see the vault, Passes)
    public const uint PlacementWords = 0x3BCDCA0;      // runner ids, defensive-line word at +0xE (int16 per side)
    public const uint OffsideLines = 0x3BE0A10;        // one float per side
    public const uint AttackLevels = 0x3BE0A6E;        // one byte per side
    const int KickWindowMs = 500, HistoryMs = 400;

    public readonly byte[] Data = new byte[PlayerCount * PlayerSize];
    public readonly byte[] Ball = new byte[0x80];
    public readonly byte[] Pad = new byte[56];
    public readonly byte[] Words = new byte[0x20];
    public readonly byte[] Offside = new byte[8];
    public readonly byte[] Attack = new byte[2];
    public byte Carrier, Restart;
    public bool Ok, BallOk;
    public long Ms;
    public readonly List<string> Kicks = [];
    public event Action<string>? KickLogged;

    readonly Queue<(long Ms, uint Buttons, int Receiver, int Gauge, int Genre, uint Pressed)>[] history =
        Enumerable.Range(0, PlayerCount).Select(_ => new Queue<(long, uint, int, int, int, uint)>()).ToArray();
    readonly byte[] lastState = new byte[PlayerCount];
    readonly long[] lastCarried = Enumerable.Repeat(long.MinValue / 2, PlayerCount).ToArray();   // only a player who just had the ball kicks it
    readonly List<PendingKick> pending = [];

    public int Int(int id, int offset) => BitConverter.ToInt32(Data, (id - 1) * PlayerSize + offset);
    public float Float(int id, int offset) => BitConverter.ToSingle(Data, (id - 1) * PlayerSize + offset);
    public byte Byte(int id, int offset) => Data[(id - 1) * PlayerSize + offset];
    public int BallSpeed => BitConverter.ToInt32(Ball, BallSpeed0);
    const int BallSpeed0 = GameLayout.BallSpeed;
    public float BallX => BitConverter.ToSingle(Ball, 0x10);
    public float BallH => BitConverter.ToSingle(Ball, 0x14);
    public float BallZ => BitConverter.ToSingle(Ball, 0x18);
    public int BallVzInt => BitConverter.ToInt32(Ball, 0x30);
    public float BallVzFloat => BitConverter.ToSingle(Ball, 0x30);

    /// <summary>Unit direction the ball is moving in (from its position change between two reads; no angle convention needed); held while it moves.</summary>
    public float DirX, DirZ;
    float prevBallX, prevBallZ;

    /// <summary>The raw pad structures (decompiled 0x8B7D10): per pad 0x58 bytes, a button word (0 = pressed) then 4 raw axes (0x80 = centre).</summary>
    public const uint RawPad = 0x3A5C390; const int RawSize = 0x58;
    public readonly byte[] Raw = new byte[RawSize * 2];
    readonly byte[] loggedRaw = new byte[RawSize * 2];
    public string Label = "";
    public event Action<string>? PadLogged;

    /// <summary>The game's own stick tables (written by 0x4B2300): per pad 4 bytes, angle word, amplitude 0-8, direction bits. Left at 0x1016680, right at 0x10165A0.</summary>
    public readonly byte[] LeftTable = new byte[8], RightTable = new byte[8];

    /// <summary>Both sticks' angle and amplitude from the tables, and the orientation words of the carrier (+0x26, +0x28, +0x42).</summary>
    public string StickText(int pad)
    {
        var c = Carrier is >= 1 and <= 22 ? Carrier : 0;
        var heading = c == 0 ? "-" : $"{BitConverter.ToUInt16(Data, (c - 1) * PlayerSize + 0x26):X4}/{BitConverter.ToUInt16(Data, (c - 1) * PlayerSize + 0x28):X4}/{BitConverter.ToUInt16(Data, (c - 1) * PlayerSize + 0x42):X4}";
        return $"gauche={BitConverter.ToUInt16(LeftTable, pad * 4):X4}/{LeftTable[pad * 4 + 2]} droit={BitConverter.ToUInt16(RightTable, pad * 4):X4}/{RightTable[pad * 4 + 2]} porteur{c}+26/28/42={heading}";
    }

    public static string RawText(byte[] raw, int pad) =>
        $"manette{pad} boutons={(ushort)~BitConverter.ToUInt16(raw, pad * RawSize):X4} axes={raw[pad * RawSize + 2]:X2} {raw[pad * RawSize + 3]:X2} {raw[pad * RawSize + 4]:X2} {raw[pad * RawSize + 5]:X2}";

    /// <summary>Logs a pad line when a button changes or an axis moves by 8 or more since the last logged line.</summary>
    void LogRaw()
    {
        for (var pad = 0; pad < 2; pad++)
        {
            var o = pad * RawSize;
            var changed = BitConverter.ToUInt16(Raw, o) != BitConverter.ToUInt16(loggedRaw, o);
            for (var a = 2; a < 6; a++) changed |= Math.Abs(Raw[o + a] - loggedRaw[o + a]) >= 8;
            if (!changed) continue;
            Array.Copy(Raw, o, loggedRaw, o, 6);
            PadLogged?.Invoke($"{TimeSpan.FromMilliseconds(Ms):mm\\:ss\\.ff} [{Label}] {RawText(Raw, pad)} {StickText(pad)}");
        }
    }

    /// <summary>
    /// -GuidePass: while a button other than the d-pad, L1 and R1 is down (and 700 ms after), every byte that changes in the passer's record (the
    /// carrier when the window opened) and in pad 0's raw structure, one line per frame, to see what counts the time the pass button is held.
    /// </summary>
    public bool PassLog;
    public event Action<string>? PassLogged;
    readonly byte[] prevCar = new byte[PlayerSize], prevPad = new byte[RawSize];
    int passer;
    long passUntil = -1;

    void LogPass()
    {
        if (!PassLog) return;
        var held = (ushort)~BitConverter.ToUInt16(Raw, 0);
        if ((held & 0xF3FF & ~0xF0) != 0) passUntil = Ms + 700;
        if (Ms > passUntil) { passer = 0; return; }
        var c = passer != 0 ? passer : Carrier is >= 1 and <= 22 ? Carrier : 0;
        if (c == 0) return;
        var car = new byte[PlayerSize];
        Array.Copy(Data, (c - 1) * PlayerSize, car, 0, PlayerSize);
        var changes = new List<string>();
        if (passer == 0)
        {
            passer = c;
            changes.Add($"start boutons={held:X4}");
        }
        else
        {
            for (var i = 0; i < PlayerSize; i++) if (car[i] != prevCar[i]) changes.Add($"+{i:X3}={prevCar[i]:X2}>{car[i]:X2}");
            for (var i = 0; i < RawSize; i++) if (Raw[i] != prevPad[i]) changes.Add($"pad+{i:X2}={prevPad[i]:X2}>{Raw[i]:X2}");
        }
        Array.Copy(car, prevCar, PlayerSize);
        Array.Copy(Raw, prevPad, RawSize);
        if (changes.Count > 0) PassLogged?.Invoke($"{TimeSpan.FromMilliseconds(Ms):mm\\:ss\\.fff} [{Label}] P{c} {string.Join(' ', changes)}");
    }

    /// <summary>-GuideRun: one line every 45 ms about the carrier (state, speed, position), the ball, the left stick and the F1 toggle byte of the patcher's debug build (0xBC3FF0).</summary>
    public bool RunLog;
    public event Action<string>? RunLogged;
    long lastRunMs = -1000;
    readonly byte[] toggle = new byte[1];

    void LogRun()
    {
        if (Ms - lastRunMs < 45) return;
        lastRunMs = Ms;
        var c = Carrier is >= 1 and <= 22 ? Carrier : 0;
        if (c == 0) return;
        if (gameForRun != null) gameForRun.TryRead(0xBC3FF0, toggle);
        // what the dribble state's handler (0x5ADA90) compares to end the dribble (decompiled 2026-11-21): the ball's +0x30 against the float
        // at 0xB8AD5C, and a threshold from the player's stat word (+0x3BCF5CC, bits 2-7) against the ball's +0x7FC; 0x3BE09FC is read first
        var extra = new byte[16]; var stat = new byte[4]; var thr0 = new byte[4]; var input = new byte[4]; var b7fc = new byte[4];
        if (gameForRun != null && ballAddress != 0)
        {
            gameForRun.TryRead(ballAddress + 0x7FC, b7fc); gameForRun.TryRead(0xB8AD5C, thr0); gameForRun.TryRead(0x3BE09FC, input);
            gameForRun.TryRead(0x3BCF5CC + (uint)((Byte(c, Team) * 0x20 + Byte(c, Slot)) * 0x348), stat);
        }
        int s = (BitConverter.ToInt32(stat, 0) >> 2) & 0x3F, thr = (-0x94 - s) * 0x60 / 0xB7;
        var cond = $" vz={BallVzFloat:F0}<={BitConverter.ToSingle(thr0, 0):F0}:{(BallVzFloat <= BitConverter.ToSingle(thr0, 0) ? 1 : 0)} thr={thr}<=b7fc={BitConverter.ToSingle(b7fc, 0):F0}:{(thr <= BitConverter.ToSingle(b7fc, 0) ? 1 : 0)} in09fc={BitConverter.ToInt32(input, 0)}";
        float dx = Float(c, X) - BallX, dz = Float(c, Z) - BallZ;
        RunLogged?.Invoke($"{TimeSpan.FromMilliseconds(Ms):mm\\:ss\\.ff} [{Label}] P{c} etat={Byte(c, State)} vit={Int(c, Speed)} pos=({Float(c, X):F0},{Float(c, Z):F0}) balle=({BallX:F0},{BallZ:F0}) d={MathF.Sqrt(dx * dx + dz * dz):F0} vballe={BallSpeed} " +
                          $"gauche={BitConverter.ToUInt16(LeftTable, 0):X4}/{LeftTable[2]} brut={Raw[4]:X2} {Raw[5]:X2} R1={((~BitConverter.ToUInt16(Raw, 0) & 0x800) != 0 ? 1 : 0)} f1={toggle[0]} b={Int(c, Buttons):X8} t={DateTime.Now:HH:mm:ss.f}{cond}");
    }
    IGameMemory? gameForRun;
    uint ballAddress;

    public void Clear() { Kicks.Clear(); lock (Calls) Calls.Clear(); }

    /// <summary>The last calls of the ball kick function 0x478020 (filled by the debugger thread, shown on the Ctrl+K page).</summary>
    public readonly List<string> Calls = [];
    public event Action<string>? CallLogged;

    /// <summary>A marker line in the call list and log (Ctrl+1 to Ctrl+4), to tell which gesture the next calls belong to.</summary>
    public void Mark(string label, long ms)
    {
        var line = $"{TimeSpan.FromMilliseconds(ms):mm\\:ss\\.f} ==== {label} ====";
        lock (Calls) { Calls.Add(line); if (Calls.Count > 14) Calls.RemoveAt(0); }
        CallLogged?.Invoke(line);
    }

    /// <summary>One call of the kick function, seen at its entry: <paramref name="esp"/> points at the return address, then the player and the arguments.</summary>
    public void AddCall(IGameMemory game, uint esp, long ms)
    {
        var st = game.ReadOrZero(esp, 64);
        uint ret = BitConverter.ToUInt32(st, 0), p = BitConverter.ToUInt32(st, 4);
        var pl = game.ReadOrZero(p, 0x20);
        int Arg(int i) => BitConverter.ToInt32(st, 8 + 4 * i);
        var callers = new List<string>();
        for (var i = 2; i < 16 && callers.Count < 2; i++) { var v = BitConverter.ToUInt32(st, 4 * i); if (v is > 0x401000 and < 0x8C0000) callers.Add($"{v:X}"); }
        var buttons = BitConverter.ToUInt32(game.ReadOrZero(p + 0xB0, 4), 0);
        var pad = game.ReadOrZero(PadMemory, 16);   // pad 1: pressed at +0, held at +8
        var line = $"{TimeSpan.FromMilliseconds(ms):mm\\:ss\\.f} P{((long)p - 0x3BDC980) / PlayerSize} etat={pl[State]} ret={ret:X} par={string.Join(",", callers)} " +
                   $"dir={Arg(0)} vit={Arg(1)} haut={Arg(2)} drap={Arg(3)} boutons={buttons:X8} manette={BitConverter.ToUInt32(pad, 8):X8}/{BitConverter.ToUInt32(pad, 0):X8}";
        lock (Calls) { Calls.Add(line); if (Calls.Count > 14) Calls.RemoveAt(0); }
        CallLogged?.Invoke(line);
    }

    public void Update(IGameMemory game, long ms)
    {
        Ms = ms;
        Ok = game.TryRead(GameLayout.Players, Data);
        if (!Ok) return;
        var one = new byte[1];
        if (game.TryRead(GameLayout.Carrier, one)) Carrier = one[0];
        if (game.TryRead(RestartState, one)) Restart = one[0];
        game.TryRead(PadMemory, Pad);
        game.TryRead(0x1016680, LeftTable); game.TryRead(0x10165A0, RightTable);
        if (game.TryRead(RawPad, Raw)) { LogRaw(); LogPass(); }
        game.TryRead(PlacementWords, Words);
        game.TryRead(OffsideLines, Offside);
        game.TryRead(AttackLevels, Attack);
        var bp = BallAddress(game);
        ballAddress = bp;
        BallOk = bp != 0 && game.TryRead(bp, Ball);
        if (!BallOk) Array.Clear(Ball);
        else
        {
            float dx = BallX - prevBallX, dz = BallZ - prevBallZ, len = MathF.Sqrt(dx * dx + dz * dz);
            if (len > 30 && len < 5000) { DirX = dx / len; DirZ = dz / len; }
            prevBallX = BallX; prevBallZ = BallZ;
        }
        Track();
        if (RunLog) { gameForRun = game; LogRun(); }
    }

    void Track()
    {
        for (var id = 1; id <= PlayerCount; id++)
        {
            var h = history[id - 1];
            var receiver = Byte(id, 0x48);
            h.Enqueue((Ms, (uint)Int(id, Buttons), receiver is >= 1 and <= 22 ? receiver : 0, Byte(id, 0x4D), Byte(id, 0x4C), (uint)Int(id, 0xB4)));
            while (h.Count > 0 && Ms - h.Peek().Ms > HistoryMs) h.Dequeue();
            var state = Byte(id, State);
            if (Carrier == id) lastCarried[id - 1] = Ms;
            if (state != lastState[id - 1] && IsKickState(state) && Ms - lastCarried[id - 1] <= 500 && !pending.Any(p => p.Player == id))
            {
                var p = new PendingKick { StartMs = Ms, Player = id, State = state, Run = Int(id, Speed), StartBallX = BallX, StartBallZ = BallZ };
                foreach (var e in h) { p.Buttons |= e.Buttons; p.Pressed |= e.Pressed; p.Gauge = Math.Max(p.Gauge, e.Gauge); if (e.Receiver != 0) { p.Receiver = e.Receiver; p.Genre = e.Genre; } }
                pending.Add(p);
            }
            lastState[id - 1] = state;
        }
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            var p = pending[i];
            if (BallSpeed > p.PeakSpeed) { p.PeakSpeed = BallSpeed; p.VzAtPeak = BallVzInt; p.VzFloatAtPeak = BallVzFloat; }
            var r = Byte(p.Player, 0x48);
            if (r is >= 1 and <= 22) p.Receiver = r;
            p.Gauge = Math.Max(p.Gauge, Byte(p.Player, 0x4D));
            p.TargetX = Float(p.Player, 0x9C); p.TargetZ = Float(p.Player, 0xA0);
            if (Ms - p.StartMs < KickWindowMs) continue;
            pending.RemoveAt(i);
            p.EndBallX = BallX; p.EndBallZ = BallZ;
            var line = Describe(p);
            Kicks.Add(line);
            if (Kicks.Count > 12) Kicks.RemoveAt(0);
            KickLogged?.Invoke(line);
        }
    }

    static bool IsKickState(byte s) => s is 4 or 5 or 6 or 8 or 9 or 27 or 30 or 34 or 44 or 48;

    public static string KickKind(int state, uint buttons, int receiver, bool towardGoal = false)
    {
        var kind = state switch
        {
            4 => "tir",
            5 => towardGoal ? "tir en course" : "passe courte",
            6 => "passe en profondeur",
            8 => "passe en hauteur",
            9 => "centre",
            27 => "passe au sol",
            30 or 34 => "tete",
            44 or 48 => "coup de pied arrete",
            _ => $"etat {state}",
        };
        if ((buttons & 0x01000C00) == 0x01000C00) kind += " +L1+R1";
        else if ((buttons & 0x400) != 0) kind += " +L1";
        return kind;
    }

    string Describe(PendingKick p)
    {
        var who = $"P{p.Player}(t{Byte(p.Player, Team)}s{Byte(p.Player, Slot)})";
        // The caller of the kick function (measured 2026-11-20 with calls 0x478020) says the gesture: 0x5BBC9F is a shot (state 4), 0x71042D7E a short or
        // through pass (state 5 / 6), 0x56A585 a lofted pass (8), 0x587C90 a cross (9), 0x562A70 a header (34). State 5 with the shot button (0x8000) is a shot on the run.
        var goal = (p.Buttons | p.Pressed) is var b && p.State == 5 && (b & 0x8000) != 0 && (b & 0x4000) == 0;
        var receiver = p.Receiver != 0 && p.State != 4 && !goal ? $" -> P{p.Receiver}" : "";
        return $"{TimeSpan.FromMilliseconds(p.StartMs):mm\\:ss\\.f} {who} {KickKind(p.State, p.Buttons, p.Receiver, goal)}{receiver} | state={p.State} " +
               $"buttons={p.Buttons:X8} pressed={p.Pressed:X8} run={p.Run} gauge={p.Gauge} genre={p.Genre} target=({p.TargetX:F0},{p.TargetZ:F0}) " +
               $"ball speed={p.PeakSpeed} vz={p.VzFloatAtPeak:F0} from ({p.StartBallX:F0},{p.StartBallZ:F0})";
    }

    public static string ModeName(int m) => m switch
    {
        1 or 2 or 3 => "arret", 7 => "general", 8 => "attaque", 9 => "dedoubl.1", 0xA => "dedoubl.2", 0xB => "dedoubl.3",
        0xF => "COURSE", 0x1D => "couloirs", 0x24 => "defense", 0x25 => "gardien", 0x27 => "marquage", 0x35 => "couverture", _ => $"0x{m:X}",
    };

    public static string PadNames(uint v)
    {
        var s = new StringBuilder();
        void Bit(uint mask, string name) { if ((v & mask) == mask) s.Append(name).Append(' '); }
        Bit(0x4000, "PASSE"); Bit(0x400, "L1"); Bit(0x800, "R*"); Bit(0x80, "D"); Bit(0x20, "G"); Bit(0x10, "-z"); Bit(0x40, "+z");
        return s.ToString().TrimEnd();
    }

    public static bool IsShotLine(string kick) => kick.Contains(") tir") || kick.Contains(") tete") || kick.Contains(") coup de pied");

    /// <summary>The kicks of the page: shots (shot, header, set piece) or passes (pass, cross); all of them on the other pages.</summary>
    public IEnumerable<string> KicksFor(HudPage page) => page switch
    {
        HudPage.Shots => Kicks.Where(IsShotLine),
        HudPage.Passes => Kicks.Where(k => !IsShotLine(k)),
        _ => Kicks,
    };

    /// <summary>The text of the live panel for a page (the kicks follow it).</summary>
    public List<string> LiveLines(HudPage page)
    {
        var l = new List<string>();
        if (!Ok) { l.Add("pas de match (memoire illisible)"); return l; }
        var c = Carrier is >= 1 and <= 22 ? Carrier : 0;
        var hp = BitConverter.ToUInt32(Pad, 8); var pp = BitConverter.ToUInt32(Pad, 0);
        var state = $"arret={Restart}  porteur={(c == 0 ? "-" : $"P{c}")}  etat={(c == 0 ? "-" : Byte(c, State).ToString())}  mode={(c == 0 ? "-" : ModeName(Byte(c, PlacementMode)))}";
        var ball = $"balle x={BallX:F0} z={BallZ:F0} h={-BallH:F0}  vit={BallSpeed}  vz={BallVzFloat:F0}";
        var pad = $"manette1 tenu={hp:X8} {PadNames(hp)}  appui={pp:X8}";
        var carrier = c == 0 ? null : $"porteur: jauge+4D={Byte(c, 0x4D)} recev+48={Byte(c, 0x48):X2} genre+4C={Byte(c, 0x4C)} boutons={Int(c, Buttons):X8} {PadNames((uint)Int(c, Buttons))}";
        var lines = $"ligne def mot  camp0={BitConverter.ToInt16(Words, 0xE)} camp1={BitConverter.ToInt16(Words, 0x10)}  hors-jeu {BitConverter.ToSingle(Offside, 0):F0} / {BitConverter.ToSingle(Offside, 4):F0}  attaque {Attack[0]}/{Attack[1]}";
        var runners = Enumerable.Range(1, PlayerCount).Where(i => Byte(i, PlacementMode) is 0xF or 9 or 0xA or 0xB)
            .Select(i => $"P{i}:{ModeName(Byte(i, PlacementMode))}");
        var runnerLine = "coureurs: " + (string.Join(" ", runners) is { Length: > 0 } r ? r : "-");
        switch (page)
        {
            case HudPage.Shots:
                l.Add("TIRS  " + (c == 0 ? "porteur -" : $"porteur P{c}  jauge={Byte(c, 0x4D)}/128  boutons={Int(c, Buttons):X8} {PadNames((uint)Int(c, Buttons))}"));
                l.Add($"balle vit={BallSpeed}  vz={BallVzFloat:F0}  h={-BallH:F0}");
                break;
            case HudPage.Passes:
                l.Add("PASSES  " + (c == 0 ? "porteur -" : $"porteur P{c}  etat={Byte(c, State)}  jauge={Byte(c, 0x4D)}/128  receveur={Byte(c, 0x48):X2}  genre={Byte(c, 0x4C)}"));
                l.Add(pad);
                l.Add($"balle vit={BallSpeed}  vz={BallVzFloat:F0}  h={-BallH:F0}");
                break;
            case HudPage.Calls:
                l.Add("APPELS de la frappe 0x478020  (lancer hud -Calls)");
                l.Add(pad);
                if (carrier != null) l.Add(carrier);
                break;
            case HudPage.Lines:
                l.Add("LIGNES / PLACEMENT"); l.Add(lines); l.Add(runnerLine);
                break;
            case HudPage.Ball:
                l.Add(state); l.Add(ball); l.Add(pad);
                if (carrier != null) l.Add(carrier);
                break;
            default:
                l.Add(state); l.Add(ball); l.Add(pad);
                if (carrier != null) l.Add(carrier);
                l.Add(lines); l.Add(runnerLine);
                break;
        }
        var up = Enumerable.Range(1, PlayerCount).Where(i => Byte(i, PlacementMode) == 0xF).Select(i => $"P{i} (camp {(i <= 11 ? 0 : 1)})").ToList();
        if (up.Count > 0) l.Insert(0, "MONTE : " + string.Join("  ", up));   // the forward runner (placement mode 0x0F), drawn in magenta by the overlay
        return l;
    }
}

/// <summary>What the overlay shows (Ctrl+S, Ctrl+P, Ctrl+L, Ctrl+B, Ctrl+A).</summary>
enum HudPage { All, Shots, Passes, Lines, Ball, Calls }
/// <summary>The overlay window: a transparent-to-clicks topmost panel placed over the game window.</summary>
sealed class HudForm : Form
{
    const int WsExLayered = 0x80000, WsExTransparent = 0x20, WsExNoActivate = 0x08000000, WsExToolWindow = 0x80;
    const int VkInsert = 0x2D, VkDelete = 0x2E;
    readonly GameProcess game;
    readonly HudTracker tracker;
    readonly System.Diagnostics.Stopwatch clock;
    readonly System.Windows.Forms.Timer timer = new();
    readonly long until;
    readonly Font font = new("Consolas", 9f), small = new("Consolas", 8f);
    bool shown = true, insertWas, deleteWas;
    HudPage page = HudPage.Shots;
    readonly Dictionary<int, bool> keyWas = [];

    // The guided capture (-Guide): what to do with the pad, step by step, with a beep at each change.
    static readonly (string Label, int Seconds)[] StickSteps =
    [
        ("PREPARE-TOI : balle au pied, immobile", 8),
        ("COURS tout droit, stick GAUCHE tenu (une direction)", 8),
        ("en courant: stick DROIT dans la MEME direction (3 fois)", 14),
        ("en courant: stick DROIT dans la direction OPPOSEE (3 fois)", 14),
        ("en courant: stick DROIT sur le COTE (3 fois)", 14),
        ("FINI, merci", 5),
    ];
    // -GuideRun (bugs #58 and #59): run with the left stick, let go, again; then the same with R1 (sprint).
    static readonly (string Label, int Seconds)[] RunSteps =
    [
        ("PREPARE-TOI : balle au pied, immobile", 8),
        ("COURS : stick GAUCHE a fond, une direction", 5), ("RELACHE le stick, ne touche plus a rien", 4),
        ("COURS : CROIX a fond, une direction", 5), ("RELACHE la croix, ne touche plus a rien", 4),
        ("COURS : stick GAUCHE a fond, une direction", 5), ("RELACHE le stick, ne touche plus a rien", 4),
        ("COURS : CROIX a fond, une direction", 5), ("RELACHE la croix, ne touche plus a rien", 4),
        ("COURS : stick GAUCHE a fond, une direction", 5), ("RELACHE le stick, ne touche plus a rien", 4),
        ("COURS : CROIX a fond, une direction", 5), ("RELACHE la croix, ne touche plus a rien", 4),
        ("FINI, merci", 5),
    ];
    // -GuidePass (issue #49): ground passes held for set times, to find what the game counts while the pass button is down.
    static readonly (string Label, int Seconds)[] PassSteps =
    [
        ("PREPARE-TOI : balle au pied, immobile", 8),
        ("PASSE COURTE : appui BREF (tap)", 6), ("PASSE COURTE : appui BREF (tap)", 6), ("PASSE COURTE : appui BREF (tap)", 6),
        ("PASSE COURTE : appui MOYEN, 0,5 s", 6), ("PASSE COURTE : appui MOYEN, 0,5 s", 6), ("PASSE COURTE : appui MOYEN, 0,5 s", 6),
        ("PASSE COURTE : appui LONG, 1 s", 7), ("PASSE COURTE : appui LONG, 1 s", 7), ("PASSE COURTE : appui LONG, 1 s", 7),
        ("FINI, merci", 5),
    ];
    // -GuideCross (issue #56): crosses from the wings at set distances, to read the cross probe's log (ring at 0x3D59E00, count at 0x3D59F08).
    static readonly (string Label, int Seconds)[] CrossSteps =
    [
        ("PREPARE-TOI : balle a l'aile, joueur arrete", 8),
        ("CENTRE 1 : APPUI BREF sur rond", 12), ("CENTRE 2 : APPUI LONG sur rond (1 s, relache apres)", 12),
        ("CENTRE 3 : APPUI BREF sur rond", 12), ("CENTRE 4 : APPUI LONG sur rond (1 s, relache apres)", 12),
        ("CENTRE 5 : APPUI BREF sur rond", 12), ("CENTRE 6 : APPUI LONG sur rond (1 s, relache apres)", 12),
        ("CENTRE 7 : APPUI BREF sur rond", 12), ("CENTRE 8 : APPUI LONG sur rond (1 s, relache apres)", 12),
        ("FINI, merci", 5),
    ];
    // -GuideSteer (issue #56): crosses aimed with the left stick (memory patch cross-steer); each started cross is logged by SampleSteer.
    static readonly (string Label, int Seconds)[] SteerSteps =
    [
        ("PREPARE-TOI : balle a l'aile", 10),
        ("CENTRE 1 : SANS stick, joueur TOURNE vers la surface, appui BREF", 20),
        ("CENTRE 2 : SANS stick, joueur TOURNE vers la surface, appui MOYEN", 20),
        ("CENTRE 3 : SANS stick, joueur FACE a la ligne de fond, appui BREF", 20),
        ("CENTRE 4 : stick PREMIER poteau, appui BREF", 20),
        ("CENTRE 5 : stick SECOND poteau, appui MOYEN", 20),
        ("CENTRE 6 : stick EN RETRAIT, appui BREF", 20),
        ("CENTRE 7 : stick vers une zone VIDE, appui MOYEN", 20),
        ("FINI, merci", 5),
    ];
    readonly bool steerProbe;
    readonly bool[] steerWas = new bool[23];
    readonly List<string> steerLines = [];
    (int Passer, int Receiver, float X, float Z, int Gauge, bool High, long Ms)? steerFlight;

    /// <summary>Logs each cross that starts (a player enters state 9 with a kick phase): passer, stick, receiver, aimed point, and the angle between the stick and the aimed point.</summary>
    void SampleSteer()
    {
        // the fall of the last cross: the ball back near the ground (its height grows downwards, about -23 on the ground) after having been up
        if (steerFlight is { } f)
        {
            if (tracker.BallH < -300) steerFlight = f with { High = true };
            else if (f.High && tracker.BallH > -80)
            {
                float dx = tracker.BallX - f.X, dz = tracker.BallZ - f.Z;
                var miss = f.Receiver is >= 1 and <= 22 ? MathF.Sqrt(MathF.Pow(tracker.BallX - tracker.Float(f.Receiver, 0xE0), 2) + MathF.Pow(tracker.BallZ - tracker.Float(f.Receiver, 0xE8), 2)) : -1;
                var line = $"CHUTE P{f.Passer} jauge={f.Gauge} en ({tracker.BallX:F0},{tracker.BallZ:F0}) distance={MathF.Sqrt(dx * dx + dz * dz):F0} a {miss:F0} de P{f.Receiver} apres {(clock.ElapsedMilliseconds - f.Ms) / 1000.0:F1} s";
                tracker.Mark($"{line} | etape: {guideText}", clock.ElapsedMilliseconds);
                steerLines.Add(line); if (steerLines.Count > 8) steerLines.RemoveAt(0);
                steerFlight = null;
            }
            else if (clock.ElapsedMilliseconds - f.Ms > 6000) steerFlight = null;
        }
        for (var id = 1; id <= 22; id++)
        {
            var on = tracker.Byte(id, 0x16) == 9 && (tracker.Int(id, 0x18) & 0xFFFF) >= 0x10;
            var was = steerWas[id]; steerWas[id] = on;
            if (!on || was) continue;
            int pad = tracker.Byte(id, 0x47), receiver = tracker.Byte(id, 0x48), aim = tracker.Int(id, 0x90) & 0xFFFF;
            var stick = pad < 8 ? game.ReadOrZero((uint)(0x1016680 + pad * 4), 4) : new byte[4];
            // the stick's angle in the pitch frame, as 0x4B2F70 gives it
            var angle = (BitConverter.ToUInt16(stick, 0) + (BitConverter.ToUInt16(game.ReadOrZero(0x3BE091A, 2), 0) == 0 ? 0xC000 : 0)) & 0xFFFF;
            var off = (short)(aim - angle) * 360.0 / 65536;
            var to = receiver is >= 1 and <= 22 ? $"P{receiver} en ({tracker.Float(receiver, 0xE0):F0},{tracker.Float(receiver, 0xE8):F0})" : $"{receiver:X2}";
            var line = $"P{id} manette={pad} de ({tracker.Float(id, 0xE0):F0},{tracker.Float(id, 0xE8):F0}) -> {to} vise=({tracker.Int(id, 0x9C)},{tracker.Int(id, 0xA0)}) " +
                       $"stick={angle:X4} force={stick[2]} bits={stick[3]:X2} angleVise={aim:X4} ecart={off:F0} deg jauge={tracker.Byte(id, 0x4D)} phase={tracker.Int(id, 0x18) & 0xFFFF:X} boutons={tracker.Int(id, 0xB0):X}";
            tracker.Mark($"STEER {line} | etape: {guideText}", clock.ElapsedMilliseconds);
            steerLines.Add(line); if (steerLines.Count > 8) steerLines.RemoveAt(0);
            steerFlight = (id, receiver, tracker.Float(id, 0xE0), tracker.Float(id, 0xE8), tracker.Byte(id, 0x4D), false, clock.ElapsedMilliseconds);
        }
    }

    // -GuideCrossRun (issue #56): held crosses on the run and standing, to see frame by frame (SampleCross) why the hold is sometimes skipped.
    static readonly (string Label, int Seconds)[] CrossRunSteps =
    [
        ("PREPARE-TOI : balle a l aile", 10),
        ("CENTRE 1 : EN COURSE, appui COURT sur rond", 15), ("CENTRE 2 : EN COURSE, appui COURT sur rond", 15), ("CENTRE 3 : EN COURSE, appui COURT sur rond", 15),
        ("CENTRE 4 : EN COURSE, rond TENU jusqu au depart", 15), ("CENTRE 5 : EN COURSE, rond TENU jusqu au depart", 15),
        ("CENTRE 6 : EN SPRINT (R1), appui COURT sur rond", 15), ("CENTRE 7 : EN SPRINT (R1), appui COURT sur rond", 15), ("CENTRE 8 : EN SPRINT (R1), appui MOYEN", 15),
        ("FINI, merci", 5),
    ];
    const uint CrossRing = 0x3D59E00, CrossCount = 0x3D59F08;
    int crossSeen;
    readonly bool crossProbe;
    readonly (string Label, int Seconds)[] steps;
    readonly bool guide;

    int lastCross = -1;

    /// <summary>While the carrier is in state 9: logs each change of the hold gauge (+0x4D), the wait counter (+0x4E) and the phase (+0x18) with the time, to measure how fast the gauge really moves.</summary>
    void SampleCross()
    {
        var c = tracker.Carrier;
        var st = c is >= 1 and <= 22 ? tracker.Byte(c, 0x16) : 0;
        if (st != 8 && st != 9 && (c == 0 || (tracker.Int(c, 0xB0) & 0x2000) == 0)) { lastCross = -1; return; }
        int g = tracker.Byte(c, 0x4D), e = tracker.Byte(c, 0x4E), ph = tracker.Int(c, 0x18) & 0xFFFF, sub = tracker.Int(c, 0x1A) & 0xFFFF, c220 = tracker.Byte(c, 0x220);
        var bar = BitConverter.ToSingle(game.ReadOrZero(0x10104E0, 4), 0); var barFlags = BitConverter.ToInt32(game.ReadOrZero(0x10104DC, 4), 0) & 7;
        var key = HashCode.Combine(st, g, e, ph, sub, c220, bar, barFlags);
        if (key == lastCross) return;
        lastCross = key;
        tracker.Mark($"S9 P{c} vit={tracker.Int(c, 0x1C)} barre={bar * 128:F0}/{barFlags} etat={st} jauge={g} 4E={e} phase={ph:X} +1A={sub:X} +220={c220} boutons={tracker.Int(c, 0xB0):X}", clock.ElapsedMilliseconds);
    }

    /// <summary>Reads the cross probe's ring: each new entry is logged with the guide step, the last ones are shown on the overlay.</summary>
    string[] CrossLines()
    {
        var count = BitConverter.ToInt32(game.ReadOrZero(CrossCount, 4), 0);
        if (count < 0 || count > 100000) return ["sonde centres: compteur illisible"];
        var ring = game.ReadOrZero(CrossRing, 256);
        var lines = new List<string> { $"SONDE CENTRES : {count} centre(s)   (brut du jeu | plafond | stat | stat2 | fatigue)" };
        for (var n = Math.Max(0, count - 8); n < count; n++)
        {
            var o = (n & 15) * 16;
            int raw = BitConverter.ToInt32(ring, o), fat = BitConverter.ToInt32(ring, o + 4), s1 = BitConverter.ToInt32(ring, o + 8), s2 = BitConverter.ToUInt16(ring, o + 12);
            int gauge = ring[o + 14], g4e = ring[o + 15];   // the probe's second version: the player's +0x4D (hold gauge) and +0x4E at the kick
            var cap = (s1 * 5 + 0x1E00) * 4 + (s2 >= 0x5F ? 0x800 : 0) + fat * -0x20;
            var line = $"#{n + 1}  brut {raw}  plafond {cap}  jauge {gauge}  (+4E {g4e})  stat {s1}/{s2}  fatigue {fat}  {(raw > cap ? "-> PLAFONNE" : "-> libre")}";
            lines.Add(line);
            if (n >= crossSeen) tracker.Mark($"CENTRE #{n + 1} {line} | etape: {guideText}", clock.ElapsedMilliseconds);
        }
        crossSeen = Math.Max(crossSeen, count);
        return [.. lines];
    }
    long guideStart = -1;
    int guideStep = -1;
    string guideText = "";

    void Guide()
    {
        if (!guide) return;
        if (guideStart < 0)
        {
            guideText = "Ctrl+G pour COMMENCER le guide";
            if ((GetAsyncKeyState(0x11) & 0x8000) == 0 || (GetAsyncKeyState(0x47) & 0x8000) == 0) return;
            guideStart = clock.ElapsedMilliseconds;
        }
        var t = (clock.ElapsedMilliseconds - guideStart) / 1000.0;
        var step = 0; var begin = 0.0;
        while (step < steps.Length - 1 && t >= begin + steps[step].Seconds) { begin += steps[step].Seconds; step++; }
        var left = Math.Max(0, begin + steps[step].Seconds - t);
        guideText = $"{steps[step].Label}   {left:F0} s";
        if (step == guideStep) return;
        guideStep = step;
        tracker.Label = steps[step].Label;
        tracker.Mark(steps[step].Label, clock.ElapsedMilliseconds);
        Speak(steps[step].Label);
        Task.Run(() => { try { if (step == steps.Length - 1) { Console.Beep(660, 150); Console.Beep(880, 150); Console.Beep(1100, 300); } else Console.Beep(880 + 120 * step, 250); } catch (Exception) { } });
    }

    /// <summary>Says the step aloud (Windows SAPI through a hidden PowerShell), for when the overlay is not visible.</summary>
    static void Speak(string text)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell", $"-NoProfile -WindowStyle Hidden -Command \"(New-Object -ComObject SAPI.SpVoice).Speak('{text.Replace("'", " ")}') | Out-Null\"")
                { CreateNoWindow = true, UseShellExecute = false });
        }
        catch (Exception) { }
    }

    public HudForm(GameProcess game, HudTracker tracker, System.Diagnostics.Stopwatch clock, int every, int seconds, bool guide = false, bool runSteps = false, bool crossSteps = false, bool steerSteps = false, bool crossRunSteps = false, bool passSteps = false)
    {
        this.game = game; this.tracker = tracker; this.clock = clock; this.guide = guide; crossProbe = crossSteps; steerProbe = steerSteps;
        steps = passSteps ? PassSteps : steerSteps ? SteerSteps : crossRunSteps ? CrossRunSteps : crossSteps ? CrossSteps : runSteps ? RunSteps : StickSteps;
        until = seconds > 0 ? seconds * 1000L : long.MaxValue;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(16, 16, 20); Opacity = 0.86; DoubleBuffered = true;
        Size = new Size(640, 560);
        timer.Interval = Math.Max(5, every);
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow; return cp; }
    }

    void Tick()
    {
        if (game.HasExited || clock.ElapsedMilliseconds > until) { Close(); return; }
        var insert = (GetAsyncKeyState(VkInsert) & 0x8000) != 0;
        if (insert && !insertWas) { shown = !shown; Visible = shown; }
        insertWas = insert;
        var delete = (GetAsyncKeyState(VkDelete) & 0x8000) != 0;
        if (delete && !deleteWas) tracker.Clear();
        deleteWas = delete;
        if ((GetAsyncKeyState(0x11) & 0x8000) != 0)   // Ctrl + S / P / L / B / A picks the page
            foreach (var (key, p) in new[] { (0x53, HudPage.Shots), (0x50, HudPage.Passes), (0x4C, HudPage.Lines), (0x42, HudPage.Ball), (0x41, HudPage.All), (0x4B, HudPage.Calls) })
            {
                var down = (GetAsyncKeyState(key) & 0x8000) != 0;
                if (down && !keyWas.GetValueOrDefault(key)) page = p;
                keyWas[key] = down;
            }
        else keyWas.Clear();
        if ((GetAsyncKeyState(0x11) & 0x8000) != 0)   // Ctrl + 1..4 marks the call log: 1 normal dribble, 2 R1 x3, 3 right stick, 4 other
            foreach (var (key, label) in new[] { (0x31, "dribble normal"), (0x32, "R1 x3"), (0x33, "stick droit avant"), (0x34, "autre") })
            {
                var down = (GetAsyncKeyState(key) & 0x8000) != 0;
                if (down && !keyWas.GetValueOrDefault(key)) tracker.Mark(label, clock.ElapsedMilliseconds);
                keyWas[key] = down;
            }
        tracker.Update(game, clock.ElapsedMilliseconds);
        if (crossProbe) SampleCross();
        if (steerProbe) SampleSteer();
        Guide();
        if (!shown) return;
        if (game.MainWindow != IntPtr.Zero && GetWindowRect(game.MainWindow, out var r) && r.Left > -10000) Location = new Point(r.Left + 8, r.Top + 70);
        else Location = new Point(20, 70);   // game minimised or without a window
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var y = 4f;
        if (guide)
        {
            using var big = new Font("Consolas", 14f, FontStyle.Bold); using var lime = new SolidBrush(Color.LimeGreen);
            g.DrawString(guideText, big, lime, 4, y); y += 26;
            if (steerProbe)
                foreach (var l in steerLines) { g.DrawString(l.Length > 95 ? l[..95] : l, font, Brushes.Gold, 4, y); y += 15; }
            else if (crossProbe)
                foreach (var l in CrossLines()) { g.DrawString(l, font, Brushes.Gold, 4, y); y += 15; }
            else
                g.DrawString(HudTracker.RawText(tracker.Raw, 0) + "  |  " + HudTracker.RawText(tracker.Raw, 1), font, Brushes.White, 4, y); y += 18;
        }
        using var white = new SolidBrush(Color.White); using var yellow = new SolidBrush(Color.Gold); using var grey = new SolidBrush(Color.LightGray);
        using var magenta = new SolidBrush(Color.Magenta); using var bold = new Font(font, FontStyle.Bold);
        foreach (var line in tracker.LiveLines(page)) { if (line.StartsWith("MONTE")) g.DrawString(line, bold, magenta, 4, y); else g.DrawString(line, font, white, 4, y); y += 15; }
        y += 4;
        if (page != HudPage.Lines && page != HudPage.Ball) { g.DrawString("Ctrl+S tirs  P passes  L lignes  B balle  K appels  A tout | Insert cacher, Suppr effacer", small, yellow, 4, y); y += 14; }
        if (page == HudPage.Calls)
        {
            string[] calls; lock (tracker.Calls) calls = [.. tracker.Calls];
            foreach (var k in calls) { g.DrawString(k.Length > 112 ? k[..112] : k, small, grey, 4, y); y += 13; }
        }
        else if (page != HudPage.Lines && page != HudPage.Ball)
            foreach (var k in tracker.KicksFor(page).TakeLast(6)) { g.DrawString(k.Length > 112 ? k[..112] : k, small, grey, 4, y); y += 13; }
        if (page is HudPage.Lines or HudPage.Passes or HudPage.All)
            DrawMap(g, new Rectangle(4, (int)y + 6, Width - 8, Height - (int)y - 10));
    }

    void DrawMap(Graphics g, Rectangle r)
    {
        if (!tracker.Ok || r.Height < 40) return;
        using var line = new Pen(Color.FromArgb(120, 120, 120)); using var red = new SolidBrush(Color.OrangeRed); using var blue = new SolidBrush(Color.LimeGreen);
        g.DrawRectangle(line, r);
        const float halfX = 15000, halfZ = 10000;
        PointF Map(float x, float z) => new(r.Left + (Math.Clamp(x, -halfX, halfX) + halfX) / (2 * halfX) * r.Width,
                                            r.Top + (Math.Clamp(z, -halfZ, halfZ) + halfZ) / (2 * halfZ) * r.Height);
        using var dash = new Pen(Color.Gold) { DashStyle = DashStyle.Dash };
        for (var side = 0; side < 2; side++)
        {
            var x = BitConverter.ToSingle(tracker.Offside, side * 4);
            if (float.IsFinite(x)) { var a = Map(x, -halfZ); g.DrawLine(side == 0 ? dash : line, a.X, r.Top, a.X, r.Bottom); }
        }
        for (var id = 1; id <= PlayerCount; id++)
        {
            var p = Map(tracker.Float(id, X), tracker.Float(id, Z));
            var mode = tracker.Byte(id, PlacementMode);
            var brush = id <= 11 ? red : blue;
            g.FillEllipse(brush, p.X - 4, p.Y - 4, 8, 8);
            if (mode == 0xF)   // the runner: thick magenta ring, a line to where he is heading, a bold label
            {
                using var ring = new Pen(Color.Magenta, 3); g.DrawEllipse(ring, p.X - 9, p.Y - 9, 18, 18);
                var to = Map(tracker.Float(id, 0x6C), tracker.Float(id, 0x70));
                if (float.IsFinite(to.X) && float.IsFinite(to.Y)) g.DrawLine(ring, p.X, p.Y, to.X, to.Y);
            }
            if (id == tracker.Carrier) g.DrawEllipse(Pens.Yellow, p.X - 6, p.Y - 6, 12, 12);
            g.DrawString(id.ToString(), small, Brushes.White, p.X + 4, p.Y - 6);
        }
        var b = Map(tracker.BallX, tracker.BallZ);
        // blue arrow from the ball: direction of travel, length = ball speed (35000 = 150 px)
        if (tracker.BallSpeed > 3000 && (tracker.DirX != 0 || tracker.DirZ != 0))
        {
            var len = Math.Min(tracker.BallSpeed, 40000) / 35000f * 150f;
            using var arrow = new Pen(Color.FromArgb(90, 150, 255), 3) { EndCap = LineCap.ArrowAnchor };
            float ax = tracker.DirX * r.Width / (2 * halfX), az = tracker.DirZ * r.Height / (2 * halfZ), al = MathF.Sqrt(ax * ax + az * az);
            if (al > 0) g.DrawLine(arrow, b.X, b.Y, b.X + ax / al * len, b.Y + az / al * len);
        }
        g.FillEllipse(Brushes.White, b.X - 3, b.Y - 3, 6, 6);
    }

    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); font.Dispose(); small.Dispose(); } base.Dispose(disposing); }

    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect r);
}
