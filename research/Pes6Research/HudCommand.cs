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
    public const string Usage = "hud [-Text] [-Seconds N] [-Log hud-events.log] [-Every 15]\n" +
                                "                                                 debug overlay over the game window (Insert = show/hide, Delete = clear); -Text prints the kicks in the console instead";

    public static int Run(Options o, string processName)
    {
        var text = o.Flag("Text");
        var seconds = o.Int("Seconds", 0);
        var every = o.Int("Every", 15);
        var log = Path.GetFullPath(o.String("Log") ?? Path.Combine(AppContext.BaseDirectory, "hud-events.log"));
        o.CheckAllUsed();
        using var game = GameProcess.Open(processName);
        var tracker = new HudTracker();
        void Record(string line) { try { File.AppendAllText(log, line + Environment.NewLine); } catch (IOException) { } }
        tracker.KickLogged += line => { Record(line); if (text) Console.WriteLine(line); };
        Record($"# hud started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        if (!text) Console.WriteLine($"overlay on; kicks logged to {log}. Insert = show/hide, Delete = clear, Ctrl+C here to stop.");
        var clock = System.Diagnostics.Stopwatch.StartNew();
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
        Application.Run(new HudForm(game, tracker, clock, every, seconds));
        return 0;
    }
}

/// <summary>One kick being measured: the state change that starts it, then the ball's launch over the next 500 ms.</summary>
sealed class PendingKick
{
    public long StartMs; public int Player, State, Receiver, Genre, Gauge;
    public uint Buttons;
    public float TargetX, TargetZ, StartBallX, StartBallZ, EndBallX, EndBallZ;
    public int PeakSpeed, VzAtPeak;
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

    readonly Queue<(long Ms, uint Buttons, int Receiver, int Gauge, int Genre)>[] history =
        Enumerable.Range(0, PlayerCount).Select(_ => new Queue<(long, uint, int, int, int)>()).ToArray();
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

    public void Clear() => Kicks.Clear();

    public void Update(IGameMemory game, long ms)
    {
        Ms = ms;
        Ok = game.TryRead(GameLayout.Players, Data);
        if (!Ok) return;
        var one = new byte[1];
        if (game.TryRead(GameLayout.Carrier, one)) Carrier = one[0];
        if (game.TryRead(RestartState, one)) Restart = one[0];
        game.TryRead(PadMemory, Pad);
        game.TryRead(PlacementWords, Words);
        game.TryRead(OffsideLines, Offside);
        game.TryRead(AttackLevels, Attack);
        var bp = BallAddress(game);
        BallOk = bp != 0 && game.TryRead(bp, Ball);
        if (!BallOk) Array.Clear(Ball);
        else
        {
            float dx = BallX - prevBallX, dz = BallZ - prevBallZ, len = MathF.Sqrt(dx * dx + dz * dz);
            if (len > 30 && len < 5000) { DirX = dx / len; DirZ = dz / len; }
            prevBallX = BallX; prevBallZ = BallZ;
        }
        Track();
    }

    void Track()
    {
        for (var id = 1; id <= PlayerCount; id++)
        {
            var h = history[id - 1];
            var receiver = Byte(id, 0x48);
            h.Enqueue((Ms, (uint)Int(id, Buttons), receiver is >= 1 and <= 22 ? receiver : 0, Byte(id, 0x4D), Byte(id, 0x4C)));
            while (h.Count > 0 && Ms - h.Peek().Ms > HistoryMs) h.Dequeue();
            var state = Byte(id, State);
            if (Carrier == id) lastCarried[id - 1] = Ms;
            if (state != lastState[id - 1] && IsKickState(state) && Ms - lastCarried[id - 1] <= 500 && !pending.Any(p => p.Player == id))
            {
                var p = new PendingKick { StartMs = Ms, Player = id, State = state, StartBallX = BallX, StartBallZ = BallZ };
                foreach (var e in h) { p.Buttons |= e.Buttons; p.Gauge = Math.Max(p.Gauge, e.Gauge); if (e.Receiver != 0) { p.Receiver = e.Receiver; p.Genre = e.Genre; } }
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
            4 => towardGoal || receiver == 0 ? "tir a l'arret" : "passe (etat 4)",
            5 => towardGoal ? "tir en course" : receiver != 0 ? "passe courte" : "tir en course / passe",
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

    // A state 4 / 5 kick whose ball ends up heading for the opponent's goal mouth is a shot, whatever +0x48 still holds
    // (the receiver byte is not always cleared: super shots were logged as passes). Goal line about x = +-13400, mouth about +-940 (256 units per metre).
    bool HeadsForGoal(PendingKick p)
    {
        if (p.State is not (4 or 5)) return false;
        var firstOpponent = p.Player <= 11 ? 12 : 1;
        var keeper = Enumerable.Range(firstOpponent, 11).FirstOrDefault(i => Byte(i, Slot) == 0, firstOpponent);
        var goalX = Float(keeper, X) > 0 ? 13400f : -13400f;
        float dx = p.EndBallX - p.StartBallX, dz = p.EndBallZ - p.StartBallZ;
        if (MathF.Abs(dx) < 500 || MathF.Sign(dx) != MathF.Sign(goalX - p.StartBallX)) return false;
        return MathF.Abs(p.StartBallZ + dz * (goalX - p.StartBallX) / dx) < 3000;
    }

    string Describe(PendingKick p)
    {
        var who = $"P{p.Player}(t{Byte(p.Player, Team)}s{Byte(p.Player, Slot)})";
        var goal = HeadsForGoal(p);
        var receiver = p.Receiver != 0 && !goal ? $" -> P{p.Receiver}" : "";
        return $"{TimeSpan.FromMilliseconds(p.StartMs):mm\\:ss\\.f} {who} {KickKind(p.State, p.Buttons, p.Receiver, goal)}{receiver} | state={p.State} " +
               $"buttons={p.Buttons:X8} gauge={p.Gauge} genre={p.Genre} target=({p.TargetX:F0},{p.TargetZ:F0}) " +
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
        return l;
    }
}

/// <summary>What the overlay shows (Ctrl+S, Ctrl+P, Ctrl+L, Ctrl+B, Ctrl+A).</summary>
enum HudPage { All, Shots, Passes, Lines, Ball }
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

    public HudForm(GameProcess game, HudTracker tracker, System.Diagnostics.Stopwatch clock, int every, int seconds)
    {
        this.game = game; this.tracker = tracker; this.clock = clock;
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
            foreach (var (key, p) in new[] { (0x53, HudPage.Shots), (0x50, HudPage.Passes), (0x4C, HudPage.Lines), (0x42, HudPage.Ball), (0x41, HudPage.All) })
            {
                var down = (GetAsyncKeyState(key) & 0x8000) != 0;
                if (down && !keyWas.GetValueOrDefault(key)) page = p;
                keyWas[key] = down;
            }
        else keyWas.Clear();
        tracker.Update(game, clock.ElapsedMilliseconds);
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
        using var white = new SolidBrush(Color.White); using var yellow = new SolidBrush(Color.Gold); using var grey = new SolidBrush(Color.LightGray);
        foreach (var line in tracker.LiveLines(page)) { g.DrawString(line, font, white, 4, y); y += 15; }
        y += 4;
        if (page != HudPage.Lines && page != HudPage.Ball) { g.DrawString("Ctrl+S tirs  P passes  L lignes  B balle  A tout | Insert cacher, Suppr effacer", small, yellow, 4, y); y += 14; }
        if (page != HudPage.Lines && page != HudPage.Ball)
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
            if (mode == 0xF) g.DrawEllipse(Pens.White, p.X - 8, p.Y - 8, 16, 16);
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
