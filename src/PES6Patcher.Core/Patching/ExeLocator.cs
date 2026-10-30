namespace PES6Patcher.Core.Patching;

/// <summary>Where the display fix applies in a given PES6.exe build.</summary>
internal sealed record DisplaySites(
    int WidthGetter, int[] Width3DCalls,
    int ClipEntry, int ScreenWidthGetter,
    int CursorEntry,
    int HalfConstant, int InverseConstant, int[] HalfReaders, int[] InverseReaders,
    int ViewRects, int ConstantsArea);

/// <summary>Where the free-direction fix applies.</summary>
internal sealed record DirectionSites(int Entry, byte[] OriginalBody, int RotateBits, int DirectionBits, int StickTable, int TurnThreshold);

/// <summary>
/// Where the close-dribbling fix applies: the ball kick relay and the return addresses of its calls
/// that make the dribble touches (running and sprinting states).
/// </summary>
internal sealed record DribbleSites(int Relay, int[] RunTouches, int[] SprintTouches);

/// <summary>
/// Finds the patch locations by signature, so that builds with the same code at other addresses
/// (1.00 / 1.10, no-DVD, repacks) can be patched. Every derived address is cross-checked.
/// </summary>
internal sealed class ExeLocator
{
    private readonly PeImage _image;
    private readonly PeSection _code;
    private readonly List<string> _problems = [];

    private ExeLocator(PeImage image)
    {
        _image = image;
        _code = image.Code;
    }

    /// <summary>Signatures of the 2D layer instructions that read the 256.0 constant (operand at +2).</summary>
    private static readonly string[] HalfReaderSignatures =
    [
        "D8 25 ?? ?? ?? ?? D9 ?? ?? ?? ?? DD D8 E8 ?? ?? ?? ?? 83 C4 20 C3",
        "D8 25 ?? ?? ?? ?? D9 ?? ?? ?? ?? DD D8 E8 ?? ?? ?? ?? 83 C4 0C C6 05",
        "D8 25 ?? ?? ?? ?? D9 ?? ?? ?? ?? E8 ?? ?? ?? ?? 83 C4 0C C6",
        "D8 25 ?? ?? ?? ?? D9 44 24 08 D8 D9 DF ?? ?? ?? ?? 75 14 D9",
        "D9 05 ?? ?? ?? ?? D8 25 ?? ?? ?? ?? D9 44 24 08 D8 D9 DF E0 F6 C4 41 7A",
        "D8 25 ?? ?? ?? ?? D9 44 24 10 D8 D9 DF ?? ?? ?? ?? 75 14 D9",
        "D9 05 ?? ?? ?? ?? D8 25 ?? ?? ?? ?? D9 44 24 10 D8 D9 DF E0 F6 C4 41 7A",
        "D8 15 ?? ?? ?? ?? DF E0 F6 C4 05 7B 08 DD D8 D9 05 ?? ?? ?? ?? E8 ?? ?? ?? ?? DA 4C",
        "D8 15 ?? ?? ?? ?? DF E0 F6 C4 05 7B 08 DD D8 D9 05 ?? ?? ?? ?? E8 ?? ?? ?? ?? ?? ?? ?? ?? ?? FF",
        "D8 05 ?? ?? ?? ?? D9 1D ?? ?? ?? ?? 8B 0E",
        "D8 25 ?? ?? ?? ?? D9 59 6C 8B 06",
    ];

    private const string PatchedTurnThreshold = "66 83 FE 0C 72 ?? 8B 57 04 66 83 7A 30 3E";

    /// <summary>
    /// Ball kick relay (player, direction, speed, height): mov ecx,[esp+0xC] ; mov eax,ecx ; cdq ; ...
    /// Patched, its first 6 bytes become "jmp cave ; nop".
    /// </summary>
    private const string KickRelay = "8B 4C 24 0C 8B C1 99 83 E2 03 03 C2 8B 15 ?? ?? ?? ?? C1 F8 02 85 C9 89 42 74";
    private const string PatchedKickRelay = "E9 ?? ?? ?? ?? 90 99 83 E2 03 03 C2 8B 15 ?? ?? ?? ?? C1 F8 02 85 C9 89 42 74";

    /// <summary>
    /// Dribble touches while running: "call relay" (E8 at the given index) in the running states measured
    /// in game. The same code exists in other states (e.g. 0x5B11DE, 0x5B53EB, 0x5B5885 in the reference
    /// build); the signatures are long enough to keep only the measured ones.
    /// </summary>
    private static readonly (string Pattern, int Call)[] RunTouchSignatures =
    [
        ("8B 0D ?? ?? ?? ?? 83 C4 0C 50 51 55 57 E8 ?? ?? ?? ?? 83 C4 0C 50 55 57 E8 ?? ?? ?? ?? 8A 17 57 88 15", 24),
        ("00 50 51 33 C9 66 8B 4F 2C 51 57 E8 ?? ?? ?? ?? 33 D2 66 8B 57 2C 83 C4 0C 50 52 57 E8 ?? ?? ?? ?? 8A 07 57 A2", 28),
        ("8B 4C 24 28 41 51 57 50 56 E8 ?? ?? ?? ?? 8A 16 56 88 15", 9),
        ("03 EE BE 10 00 00 00 56 55 53 57 E8 ?? ?? ?? ?? 8A 17 57 88 15", 11),
        ("57 89 48 14 E8 ?? ?? ?? ?? 50 8B C5 E8 ?? ?? ?? ?? 8B 15 ?? ?? ?? ?? 50 52 56 57 E8 ?? ?? ?? ?? 8A 07 57 A2", 27),
    ];

    /// <summary>Dribble touches while sprinting (the direction comes from [edi+0x30]).</summary>
    private static readonly (string Pattern, int Call)[] SprintTouchSignatures =
    [
        ("83 C4 0C 50 33 C0 66 8B 47 30 50 57 E8 ?? ?? ?? ?? 8A 0F 57 88 0D", 12),
        ("33 C9 66 8B 4F 30 83 C4 0C 50 51 57 E8 ?? ?? ?? ?? 8A 17 57 88 15", 12),
    ];

    public static (DisplaySites? Display, DirectionSites? Direction, DribbleSites? Dribble, bool AlreadyPatched, List<string> Problems) Locate(byte[] exe)
    {
        var locator = new ExeLocator(new PeImage(exe));
        var (display, patched) = locator.FindDisplay();
        // a gameplay-only patch leaves the 2D width alone but lowers the turn threshold (0x20 -> 0x0C)
        // or hooks the kick relay
        patched |= locator.Hits(PatchedTurnThreshold).Count == 1 || locator.Hits(PatchedKickRelay).Count == 1;
        int displayProblems = locator._problems.Count;
        var direction = locator.FindDirection();
        if (locator._problems.Count > displayProblems) direction = null;
        int otherProblems = locator._problems.Count;
        var dribble = locator.FindDribble();
        if (locator._problems.Count > otherProblems) dribble = null;
        return (displayProblems == 0 ? display : null, direction, dribble, patched, locator._problems);
    }

    private (DisplaySites?, bool) FindDisplay()
    {
        // 2D layer width / height getters: mov eax,512 ; ret  ...  mov eax,448 ; ret
        int widthGetter = Unique("B8 ?? ?? 00 00 C3 CC CC CC CC CC CC CC CC CC CC B8 C0 01 00 00 C3", "Check.Width2D");
        if (widthGetter == 0) return (null, false);
        int width = _image.ReadInt32(widthGetter + 1);
        bool patched = width != 512;
        if (patched) return (null, true);

        // "call widthGetter ; push eax ; call <3D projection setter> ; add esp,8": exactly two of them.
        // The setter stores (width, height) for the 3D projection and raises a "changed" flag. Other
        // calls (e.g. the 2D clip region) must keep the new width.
        var setter = new Signature("8B 44 24 04 8B 4C 24 08 C6 05 ?? ?? ?? ?? 01 A3 ?? ?? ?? ?? 89 0D ?? ?? ?? ?? C3");
        var calls = Hits("E8 ?? ?? ?? ?? 50 E8 ?? ?? ?? ?? 83 C4 08")
            .Where(m => _image.Rel32Target(m) == widthGetter && setter.IsAt(_image, _image.Rel32Target(m + 6))).ToList();
        if (calls.Count != 2) Problem("Check.Width3D", calls.Count > 0 ? calls[0] : widthGetter);

        // clip rectangle -> pixels (x * screenWidth / 512)
        int clip = Unique("51 53 55 56 57 E8 ?? ?? ?? ?? 8B E8 E8 ?? ?? ?? ?? 8B C8 8B C5 0F AF 44 24 18 99 81 E2 FF 01 00 00", "Hook.Clip");
        int screenWidth = clip == 0 ? 0 : _image.Rel32Target(clip + 5);

        // 3D -> 2D layer projection: fild [esp+4C] ; fmul [esp+40] ; fstp [esp] ; push ebx ; push <matrix> ; call
        int cursor = Unique("DB 44 24 4C D8 4C 24 40 D9 1C 24 53 68 ?? ?? ?? ?? E8", "Hook.Cursor");

        // 2D layer x -> pixels: (x + 256) * screenWidth * (1/512). Its twin for y calls the height getter.
        int half = 0, inverse = 0;
        var halfReaders = new List<int>();
        var inverseReaders = new List<int>();
        var conversions = Hits("51 E8 ?? ?? ?? ?? 89 04 24 8B 44 24 08 DB 04 24 25 FF FF 00 00 89 44 24 08 DB 44 24 08 83 EC 08 " +
                               "D8 0D ?? ?? ?? ?? D8 25 ?? ?? ?? ?? D8 05 ?? ?? ?? ?? DE C9 D8 0D ?? ?? ?? ??")
            .Where(m => screenWidth != 0 && _image.Rel32Target(m + 1) == screenWidth).ToList();
        if (conversions.Count == 1)
        {
            int x = conversions[0];
            half = _image.ReadInt32(x + 0x2E);
            inverse = _image.ReadInt32(x + 0x36);
            if (_image.ReadFloat(half) != 256f || _image.ReadFloat(inverse) != 1f / 512) Problem("Check.Constants", x);
            halfReaders.Add(x + 0x2C);
            inverseReaders.Add(x + 0x34);
        }
        else Problem("Check.Operand", screenWidth);

        if (half != 0)
        {
            foreach (var pattern in HalfReaderSignatures)
            {
                var hits = Hits(pattern).Where(m => _image.ReadInt32(m + 2) == half).ToList();
                if (hits.Count == 1) halfReaders.Add(hits[0]);
                else Problem("Check.Operand", hits.Count > 0 ? hits[0] : 0);
            }
        }

        // initial 2D view rectangles (x, y, w, h) x 2, in .data
        byte[] rects = [.. Rect(), .. Rect()];
        int viewRects = FindData(rects, "Check.ViewRect");

        // private constants: last 0x100 bytes of .rdata, which must be zero padding
        int constants = 0;
        var rdata = _image.Named(".rdata");
        if (rdata != null && _image.Matches(rdata.End - 0x100, new byte[0x100])) constants = rdata.End - 0x100;
        else Problem("Check.Constants", rdata?.End ?? 0);

        if (_problems.Count > 0) return (null, false);
        return (new DisplaySites(widthGetter, [.. calls], clip, screenWidth, cursor, half, inverse,
            [.. halfReaders], [.. inverseReaders], viewRects, constants), false);
    }

    private DirectionSites? FindDirection()
    {
        // 8-way direction bits -> heading: sub esp,0x10 ; ... ; jmp [virtualised body]
        int entry = Unique("83 EC 10 8B 4C 24 14 33 C0 66 89 04 24 FF 25 ?? ?? ?? ??", "Hook.FreeDirection");
        // direction bits rotated for the attacking side
        int rotate = Unique("0F B7 0D ?? ?? ?? ?? 8A 44 24 04 24 F0 85 C9 74", "Hook.FreeDirection");

        // current human input bits: "call rotate ; add esp,4 ; mov [bits],eax", always the same global
        int bits = 0;
        if (rotate != 0)
        {
            var stores = Hits("E8 ?? ?? ?? ?? 83 C4 04 A3 ?? ?? ?? ??")
                .Where(m => _image.Rel32Target(m) == rotate).Select(m => _image.ReadInt32(m + 9)).Distinct().ToList();
            if (stores.Count == 1) bits = stores[0];
            else Problem("Hook.FreeDirection", rotate);
        }

        // analog stick table (angle, magnitude, bits per pad): mov ebp,<table+2> ; mov ebx,<pad copy>
        int stick = Unique("BD ?? ?? ?? ?? BB ?? ?? ?? ?? 0F B6 73 FE 85 F6", "Hook.FreeDirection");
        int table = stick == 0 ? 0 : _image.ReadInt32(stick + 1) - 2;

        // running state: cmp si,0x20 (45 degrees) before starting a turn
        int threshold = Unique("66 83 FE 20 72 ?? 8B 57 04 66 83 7A 30 3E", "Check.TurnThreshold");

        if (entry == 0 || rotate == 0 || bits == 0 || table == 0 || threshold == 0) return null;
        return new DirectionSites(entry, _image.Read(entry, 19), rotate, bits, table, threshold);
    }

    private DribbleSites? FindDribble()
    {
        int relay = Unique(KickRelay, "Hook.Dribble");
        if (relay == 0) return null;

        // return address of the single "call relay" each signature describes
        int[] Touches((string Pattern, int Call)[] signatures)
        {
            var found = new List<int>();
            foreach (var (pattern, call) in signatures)
            {
                var calls = Hits(pattern).Select(m => m + call).Where(c => _image.Rel32Target(c) == relay).ToList();
                if (calls.Count == 1) found.Add(calls[0] + 5);
                else Problem("Hook.Dribble", calls.Count > 0 ? calls[0] : 0);
            }
            return [.. found];
        }

        var run = Touches(RunTouchSignatures);
        var sprint = Touches(SprintTouchSignatures);
        return run.Length == RunTouchSignatures.Length && sprint.Length == SprintTouchSignatures.Length
            ? new DribbleSites(relay, run, sprint)
            : null;
    }

    private static byte[] Rect() =>
        [.. BitConverter.GetBytes(-256f), .. BitConverter.GetBytes(-224f), .. BitConverter.GetBytes(512f), .. BitConverter.GetBytes(448f)];

    private List<int> Hits(string pattern) => new Signature(pattern).FindAll(_image, _code);

    private int Unique(string pattern, string whatKey)
    {
        var hits = Hits(pattern);
        if (hits.Count == 1) return hits[0];
        Problem(whatKey, hits.Count > 0 ? hits[0] : 0);
        return 0;
    }

    private int FindData(byte[] bytes, string whatKey)
    {
        var data = _image.Named(".data");
        if (data != null)
        {
            var hits = new Signature(string.Join(' ', bytes.Select(b => b.ToString("X2")))).FindAll(_image, data);
            if (hits.Count == 1) return hits[0];
        }
        Problem(whatKey, 0);
        return 0;
    }

    private void Problem(string whatKey, int va) => _problems.Add(Loc.T("Check.Location", Loc.T(whatKey), va));
}
