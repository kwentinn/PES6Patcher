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
/// Where the dribble fixes apply: the ball kick relay and the return addresses of its calls that make
/// the dribble touches (running and sprinting states). <see cref="SnappedTouch"/>, the most frequent
/// one, gets its direction rounded to 22.5 degrees.
/// </summary>
internal sealed record DribbleSites(int Relay, int[] RunTouches, int[] SprintTouches, int SnappedTouch)
{
    /// <summary>Import slots for the in-game close dribbling switch (F1) and its Kitserver message.</summary>
    public int KeyStateImport { get; init; }
    public int ModuleHandleImport { get; init; }
    public int ProcAddressImport { get; init; }
    public int TickCountImport { get; init; }

    /// <summary>
    /// Last 16 bytes of .rdata (zero padding, made writable): +0 switched off, +1 set up, +2 message
    /// hook installed, +4 KDrawText, +8 tick until which the message shows.
    /// </summary>
    public int SwitchState { get; init; }

    /// <summary>
    /// Dribble accuracy of the players in the match: low 7 bits of the dword at this address +
    /// (player[0x12] * 32 + player[0x11]) * 0x348 (read by the game's dribble touch direction noise).
    /// </summary>
    public int DribbleAccuracy { get; init; }
}

/// <summary>
/// Where the free kick fix applies: the set piece's "call shot" (<see cref="ShotCall"/>, to <see cref="Shot"/>),
/// the ball kick function's entry, and the "fadd [weight]" of the ball flight (also used by the trajectory
/// predictions). <see cref="State"/>: the 32 bytes of .rdata before the close dribbling ones (zero padding, made
/// writable), see <see cref="CodeCaves.FreeKick"/>.
/// </summary>
internal sealed record FreeKickSites(int ShotCall, int Shot, int KickEntry, int WeightAdd, int Weight, int State)
{
    /// <summary>Import slots for the free kick weight test key (F2) and its Kitserver message.</summary>
    public int KeyStateImport { get; init; }
    public int ModuleHandleImport { get; init; }
    public int ProcAddressImport { get; init; }
    public int TickCountImport { get; init; }

    /// <summary>Analog stick table (as in <see cref="DirectionSites.StickTable"/>): the shot gets the extra weight when a stick is pushed up.</summary>
    public int StickTable { get; init; }
}

/// <summary>
/// Shot function (player, power, ..., kind), called by every shot state: its first 9 bytes (sub esp,0x14 ;
/// fld dword [<see cref="Float"/>]) can move to a cave. <see cref="SetPieceReturn"/>: return address of the set
/// piece's call (free kicks keep their own power).
/// </summary>
internal sealed record ShotSites(int Entry, int Float, int SetPieceReturn)
{
    /// <summary>Active (cursor) player id of each team: byte at team * 2 (0xFF = none).</summary>
    public int ActivePlayers { get; init; }

    /// <summary>Players of the match (0x240 bytes each; +0x12 team, +0xB0 input bits).</summary>
    public int Players { get; init; }

    /// <summary>Analog stick table (as in <see cref="DirectionSites.StickTable"/>).</summary>
    public int StickTable { get; init; }

    /// <summary>The ball kick function's entry, where the human teams are learnt.</summary>
    public int KickEntry { get; init; }

    /// <summary>
    /// Per team, how well its active player follows a pad's stick (bytes at +0 and +1), and the direction asked
    /// for the shot under way (word at +2) (.rdata zero padding, made writable).
    /// </summary>
    public int HumanScores { get; init; }

    /// <summary>The shot function's "call kick" (player, direction, speed, height, 0), after its random penalties.</summary>
    public int KickCall { get; init; }

    /// <summary>Favoured foot of the players in the match: bit 0 of the dword at this address + (player[0x12] * 32 + player[0x11]) * 0x348.</summary>
    public int FavouredFoot { get; init; }
}

/// <summary>Team spread: the function setting where a player places himself (player, x, z), and the player array.</summary>
internal sealed record SpreadSites(int Entry, int Players);

/// <summary>
/// Top speed: where each player's maximum running speed (player+0x58) is computed from the top speed stat, in an
/// executable section of the protection: <see cref="Entry"/> = lea edx,[eax+eax*2] (eax = stat above 40, in %),
/// <see cref="Store"/> = mov [esi+0x46],edx (esi = player+0x12).
/// </summary>
internal sealed record SpeedSites(int Entry, int Store);

/// <summary>
/// Sprint turns: <see cref="Limit"/> = push 0x1000, the most the ball's direction may turn towards the stick at a
/// sprint stride (22.5 degrees); <see cref="Touch"/> = push eax (speed) ; xor eax,eax ; mov ax,[edi+0x30] (direction)
/// before the first sprint touch's relay call; <see cref="OtherTouch"/> = xor ecx,ecx ; mov cx,[edi+0x30] ; add esp,0xC
/// before the other foot's; <see cref="Ball"/> = pointer to the ball (direction at +0x58); <see cref="Animation"/> = the
/// byte of cmp ax,0x50 in the sprint stride: a turn of 0x50/256 (112 degrees) or more is handed to the normal dribble
/// stride, which plays the turning animation.
/// </summary>
internal sealed record SprintTurnSites(int Limit, int Touch, int OtherTouch, int Ball, int Animation);

/// <summary>
/// Pressing challenge: in the pressing state, <see cref="Range"/> = cmp edi,0xA0 ; jge <see cref="Fail"/> ; push 0x50, before the
/// take-ball attempt (player, distance, 0x50). The attempt itself only goes on within speed / 128 + 2 * 0x50 of the ball.
/// </summary>
internal sealed record TackleSites(int Range, int Fail);

/// <summary>
/// Lofted pass aim, in the receiver choice 0x569020: <see cref="Gauge"/> = movzx eax,al ; shr eax,3 ; imul eax,eax,0x380
/// (12 bytes: the pass gauge +0x4D turned into the distance wanted, before add eax,0x800); <see cref="Angle"/> = the byte of
/// cmp word [esp+0x1C],0x20 (a receiver within 45 degrees of the stick).
/// </summary>
internal sealed record LoftedAimSites(int Gauge, int Angle);

/// <summary>
/// Pass lead: where the pass point (passer +0x9C / +0xA0) is placed ahead of the receiver, the lead (passer +0xA8) is
/// read twice: <see cref="First"/> = mov eax,[edi+0xA8] (6 bytes, for the cosine), <see cref="Second"/> = mov ecx,[edi+0xA8]
/// (6 bytes, for the sine).
/// </summary>
internal sealed record PassLeadSites(int First, int Second);

/// <summary>
/// Analog stick dead zone, in the pad reader: each of the 4 axis bytes (0-255, centre 128) is zeroed when its distance from the
/// centre is at most 0x25; <see cref="Limits"/> = the imm8 of those 4 "cmp eax,25h". A stick held within about 16 degrees of
/// an axis therefore reads as exactly on it.
/// <see cref="Aims"/>: the corner and free kick waits turn the aim [player+0xA0] by 0x40 per frame as soon as the stick's
/// right (0x80) or left (0x20) bit is set, whatever the push (the 22 bytes from "test al,al").
/// </summary>
internal sealed record StickDeadZoneSites(int[] Limits, int[] Aims);

/// <summary>
/// Carrier turn rate: running with the ball, the facing and run direction (+0x26, +0x28) turn towards their target by at
/// most a fixed step per frame (0x100 = 1.4 degrees), while a sharp touch sends the ball at once. <see cref="First"/> = the
/// imm32 of push 100h before that turn (then +0x26, +0x28 and +0x42 are written); <see cref="Second"/> = the imm32 of
/// add ecx,100h in another with-ball path (its step is halved in some animations).
/// </summary>
internal sealed record CarrierTurnSites(int First, int Second);

/// <summary>Conditional jumps (je, 2 bytes) that, made inactive, skip the opening movie and the logos.</summary>
internal sealed record IntroSites(int[] Jumps);

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
        ("8B 4C 24 28 41 51 57 50 56 E8 ?? ?? ?? ?? 8A 16 56 88 15", 9),     // SnappedTouchIndex
        ("03 EE BE 10 00 00 00 56 55 53 57 E8 ?? ?? ?? ?? 8A 17 57 88 15", 11),
        ("57 89 48 14 E8 ?? ?? ?? ?? 50 8B C5 E8 ?? ?? ?? ?? 8B 15 ?? ?? ?? ?? 50 52 56 57 E8 ?? ?? ?? ?? 8A 07 57 A2", 27),
    ];

    /// <summary>The run touch whose direction is the stick angle rounded to 22.5 degrees.</summary>
    private const int SnappedTouchIndex = 2;

    /// <summary>Dribble touches while sprinting (the direction comes from [edi+0x30]).</summary>
    private static readonly (string Pattern, int Call)[] SprintTouchSignatures =
    [
        ("83 C4 0C 50 33 C0 66 8B 47 30 50 57 E8 ?? ?? ?? ?? 8A 0F 57 88 0D", 12),
        ("33 C9 66 8B 4F 30 83 C4 0C 50 51 57 E8 ?? ?? ?? ?? 8A 17 57 88 15", 12),
    ];

    /// <summary>
    /// Dribble touch direction noise: index = player[0x12] * 32 + player[0x11] ; mov eax,[index * 0x348 +
    /// dribble accuracy] (address at +23) ; and eax,0x7F ; push 40 ; push eax ; call.
    /// </summary>
    private const string DribbleAccuracyReader = "0F B6 4F 12 0F B6 57 11 C1 E1 05 03 CA 69 C9 48 03 00 00 8B F0 8B 81 ?? ?? ?? ?? 83 E0 7F 6A 28 50 E8";

    /// <summary>
    /// Set piece (direct free kick) shot: push esi ; call ; xor edx,edx ; mov dx,[...] ; push 0 ; push edi ;
    /// push edx ; push esi ; call shot (at +20). Shots in open play call the same function from elsewhere.
    /// </summary>
    private const string FreeKickShot = "56 E8 ?? ?? ?? ?? 33 D2 66 8B 15 ?? ?? ?? ?? 6A 00 57 52 56 E8 ?? ?? ?? ?? 0F B6";
    private const int FreeKickShotCall = 20;

    /// <summary>Analog stick table reader: mov ebp,<table + 2> ; mov ebx,<pad copy> ; movzx esi,byte [ebx-2] ; test esi,esi.</summary>
    private const string StickTableReader = "BD ?? ?? ?? ?? BB ?? ?? ?? ?? 0F B6 73 FE 85 F6";

    /// <summary>Shot function entry: sub esp,0x14 ; fld dword [...]. Patched: jmp cave ; 4 x nop.</summary>
    private static readonly byte[] ShotEntry = [0x83, 0xEC, 0x14, 0xD9, 0x05];
    private const string PatchedShotEntry = "E9 ?? ?? ?? ?? 90 90 90 90 A1 ?? ?? ?? ?? 53 56 8B 74 24 20";

    /// <summary>Ball kick function (player, direction, speed, height, flag): push ebx/ebp/esi ; mov esi,[esp+0x10] ; ...</summary>
    private const string KickEntry = "53 55 56 8B 74 24 10 8A 06 33 DB 3A C3 74";
    private const string PatchedKickEntry = "E9 ?? ?? ?? ?? 90 90 8A 06 33 DB 3A C3 74";

    /// <summary>Ball flight: fdiv ; fimul ; fmul ; fadd [weight] (at +16) ; fmul [esp+0x58] ; fadd [eax] ; fstp [eax].</summary>
    private const string BallWeight = "D8 35 ?? ?? ?? ?? DA 4C 24 14 D8 0D ?? ?? ?? ?? D8 05 ?? ?? ?? ?? D8 4C 24 58 D8 00 D9 18";
    private const int BallWeightAdd = 16;

    /// <summary>
    /// Opening movie: "push edi ; xor edi,edi ; call skipRequested ; test ax,ax ; je play" - the movie
    /// function already returns early when skipping is requested (je at +11).
    /// </summary>
    private const string MovieSkip = "57 33 FF E8 ?? ?? ?? ?? 66 85 C0 74 18 6A 01 E8 ?? ?? ?? ?? 57 E8 ?? ?? ?? ?? 83 C4 08 5F 81 C4";
    private const string PatchedMovieSkip = "57 33 FF E8 ?? ?? ?? ?? 66 85 C0 90 90 6A 01 E8 ?? ?? ?? ?? 57 E8 ?? ?? ?? ?? 83 C4 08 5F 81 C4";
    private const int MovieSkipJump = 11;

    /// <summary>
    /// Logos: a logo flagged as skippable ends at once, with a fast fade, when a button is pressed
    /// (je at +21: flag ; je at +36: button). Both inactive = the game's own skip path for every logo.
    /// </summary>
    private const string LogoSkip = "8B 46 08 8D 7E 08 C1 E0 04 F6 80 ?? ?? ?? ?? 01 BB 04 00 00 00 74 1E 6A 00 E8 ?? ?? ?? ?? 83 C4 04 F6 C4 40 74 0F 8B 07";
    private static readonly int[] LogoSkipJumps = [21, 36];

    public static (DisplaySites? Display, DirectionSites? Direction, DribbleSites? Dribble, IntroSites? Intro, FreeKickSites? FreeKick, ShotSites? Shot, bool AlreadyPatched, List<string> Problems) Locate(byte[] exe)
    {
        var locator = new ExeLocator(new PeImage(exe));
        var (display, patched) = locator.FindDisplay();
        // a patch without the display fix leaves the 2D width alone but lowers the turn threshold
        // (0x20 -> 0x0C), hooks the kick relay, the kick function or the shot function, or skips the opening movie
        patched |= locator.Hits(PatchedTurnThreshold).Count == 1 || locator.Hits(PatchedKickRelay).Count == 1
            || locator.Hits(PatchedKickEntry).Count == 1 || locator.Hits(PatchedShotEntry).Count == 1 || locator.Hits(PatchedMovieSkip).Count == 1;
        int displayProblems = locator._problems.Count;
        var direction = locator.FindDirection();
        if (locator._problems.Count > displayProblems) direction = null;
        int otherProblems = locator._problems.Count;
        var dribble = locator.FindDribble();
        if (locator._problems.Count > otherProblems) dribble = null;
        otherProblems = locator._problems.Count;
        var intro = locator.FindIntro();
        if (locator._problems.Count > otherProblems) intro = null;
        otherProblems = locator._problems.Count;
        var freeKick = locator.FindFreeKick();
        if (locator._problems.Count > otherProblems) freeKick = null;
        otherProblems = locator._problems.Count;
        var shot = locator.FindShot();
        if (locator._problems.Count > otherProblems) shot = null;
        return (displayProblems == 0 ? display : null, direction, dribble, intro, freeKick, shot, patched, locator._problems);
    }

    private FreeKickSites? FindFreeKick()
    {
        int shot = Unique(FreeKickShot, "Hook.FreeKick");
        int kick = Unique(KickEntry, "Hook.FreeKick");
        int weight = Unique(BallWeight, "Hook.FreeKick");
        var rdata = _image.Named(".rdata");
        int state = rdata != null && _image.Matches(rdata.End - 0x30, new byte[0x30]) ? rdata.End - 0x30 : 0;
        int keyState = _image.ImportSlot("USER32.dll", "GetAsyncKeyState");
        int moduleHandle = _image.ImportSlot("KERNEL32.dll", "GetModuleHandleA");
        int procAddress = _image.ImportSlot("KERNEL32.dll", "GetProcAddress");
        int tickCount = _image.ImportSlot("KERNEL32.dll", "GetTickCount");
        bool imports = keyState != 0 && moduleHandle != 0 && procAddress != 0 && tickCount != 0;
        if (state == 0 || !imports) Problem("Hook.FreeKick", 0);
        int stick = Unique(StickTableReader, "Hook.FreeKick");
        if (shot == 0 || kick == 0 || weight == 0 || state == 0 || !imports || stick == 0) return null;

        // the ball weight constant is a positive float (188.0 in the reference build)
        int weightConstant = _image.ReadInt32(weight + BallWeightAdd + 2);
        if (!(_image.ReadFloat(weightConstant) is > 0f and < 10000f)) { Problem("Hook.FreeKick", weight); return null; }
        int call = shot + FreeKickShotCall;
        return new FreeKickSites(call, _image.Rel32Target(call), kick, weight + BallWeightAdd, weightConstant, state)
        {
            KeyStateImport = keyState, ModuleHandleImport = moduleHandle, ProcAddressImport = procAddress, TickCountImport = tickCount,
            StickTable = _image.ReadInt32(stick + 1) - 2,
        };
    }

    private ShotSites? FindShot()
    {
        int setPiece = Unique(FreeKickShot, "Hook.ShotPower");
        if (setPiece == 0) return null;
        int call = setPiece + FreeKickShotCall, entry = _image.Rel32Target(call);
        if (!_image.Matches(entry, ShotEntry)) { Problem("Hook.ShotPower", entry); return null; }
        int active = Unique(ActivePlayerReader, "Hook.ShotPower");
        int stick = Unique(StickTableReader, "Hook.ShotPower");
        int kick = Unique(KickEntry, "Hook.ShotPower");
        var rdata = _image.Named(".rdata");
        int scores = rdata != null && _image.Matches(rdata.End - 0x40, new byte[0x10]) ? rdata.End - 0x40 : 0;
        int kickCall = Unique(ShotKickCall, "Hook.ShotPower");
        int foot = Unique(FootReader, "Hook.ShotPower");
        if (kickCall != 0 && _image.Rel32Target(kickCall + 10) != kick) kickCall = 0;
        if (active == 0 || stick == 0 || kick == 0 || scores == 0 || kickCall == 0 || foot == 0) { Problem("Hook.ShotPower", 0); return null; }
        return new ShotSites(entry, _image.ReadInt32(entry + 5), call + 5)
        {
            ActivePlayers = _image.ReadInt32(active + 6), Players = _image.ReadInt32(active + 41),
            StickTable = _image.ReadInt32(stick + 1) - 2, KickEntry = kick, HumanScores = scores, KickCall = kickCall + 10,
            FavouredFoot = _image.ReadInt32(foot + 29),
        };
    }

    /// <summary>
    /// The game's own foot check: foot of the animation ([[player+4]+0x2D]) against the favoured foot (stat, bit 0,
    /// address at +29): movzx eax,[esi+0x11] ; mov ecx,[esi+4] ; movzx edx,[ecx+0x2D] ; ... ; and eax,1 ; cmp edx,eax.
    /// </summary>
    private const string FootReader = "0F B6 46 11 8B 4E 04 0F B6 51 2D 57 0F B6 7E 12 C1 E7 05 03 F8 69 FF 48 03 00 00 8B 87 ?? ?? ?? ?? 83 E0 01 3B D0";

    /// <summary>The shot's kick: push 0 ; push edi ; push ebx ; push ebp ; push esi ; mov [esp+0x4C],eax ; call kick (+10).</summary>
    private const string ShotKickCall = "6A 00 57 53 55 56 89 44 24 4C E8 ?? ?? ?? ?? 56 E8";

    /// <summary>
    /// The other team's active player: xor ecx,1 ; mov al,[ecx*2 + active players] (+6) ; ... ; add edx,players (+41).
    /// </summary>
    private const string ActivePlayerReader = "83 F1 01 8A 04 4D ?? ?? ?? ?? 3C FF 74 0E 0F B6 C0 99 8B 0D ?? ?? ?? ?? F7 F9 EB 05 BA 01 00 00 00 8D 14 D2 C1 E2 06 81 C2 ?? ?? ?? ??";

    /// <summary>Player target setter (player, x, z): push esi ; mov esi,[esp+8] ; cmp byte [esi+0x16],7 ; jne ; jmp [...].</summary>
    private const string TargetSetter = "56 8B 74 24 08 80 7E 16 07 0F 85 ?? ?? ?? ?? FF 25";

    /// <summary>
    /// Maximum running speed: lea edx,[eax+eax*2] ; movzx eax,[esi+0x4A] ; shl edx,2 ; shl eax,4 ; sub edx,eax ;
    /// add edx,0x1D76 ; mov [esi+0x46],edx (at +23). 7542 + 12 * (stat - 40) * 100 / 60 - 16 * player[0x5C].
    /// </summary>
    private const string MaxSpeed = "8D 14 40 0F B6 46 4A C1 E2 02 C1 E0 04 2B D0 81 C2 76 1D 00 00 89 56 46";

    public static SpeedSites? LocateSpeed(byte[] exe)
    {
        var image = new PeImage(exe);
        var hits = image.Sections.Where(s => s.IsCode).SelectMany(s => new Signature(MaxSpeed).FindAll(image, s)).ToList();
        return hits.Count == 1 ? new SpeedSites(hits[0], hits[0] + 21) : null;
    }

    /// <summary>
    /// Sprint stride: mov edx,[esi+0x58] ; xor ecx,ecx ; mov cx,[esi+0x26] ; add cx,0x800 ; ... ; push 0x1000 (at +26) ;
    /// then the facing rounded to 22.5 degrees is turned towards the stick by at most that.
    /// </summary>
    private const string SprintTurnLimit = "8B 56 58 33 C9 66 8B 4E 26 66 81 C1 00 08 8D 04 40 8D 84 98 ?? ?? ?? ?? 03 C2 68 00 10 00 00 89 44 24 44";

    /// <summary>
    /// Sprint touch: mov eax,[ball] (+1) ; ... ; push eax (speed, at +58) ; xor eax,eax ; mov ax,[edi+0x30] ; push eax ; push edi ; call relay.
    /// </summary>
    private const string SprintTouch = "A1 ?? ?? ?? ?? 8B 88 FC 07 00 00 57 89 48 14 E8 ?? ?? ?? ?? 83 C0 04 50 8B C6 E8 ?? ?? ?? ?? " +
        "8B 0D ?? ?? ?? ?? 33 D2 66 8B 57 2C 83 C4 08 50 51 52 57 E8 ?? ?? ?? ?? 83 C4 0C 50 33 C0 66 8B 47 30 50 57 E8";

    /// <summary>
    /// The other foot's sprint touch: xor ecx,ecx ; mov cx,[edi+0x30] (direction) ; add esp,0xC ; push eax (speed) ;
    /// push ecx ; push edi ; call relay.
    /// </summary>
    private const string SprintOtherTouch = "33 C9 66 8B 4F 30 83 C4 0C 50 51 57 E8 ?? ?? ?? ?? 8A 17 57 88 15";

    /// <summary>
    /// Sprint stride: cmp ax,8 ; jae ; pop edi ; xor eax,eax ; pop esi ; add esp,0x28 ; ret ; cmp ax,0x50 (byte at +16) ;
    /// jb: from 0x50/256 of a turn, the normal dribble stride takes over (turning animation).
    /// </summary>
    private const string SprintTurnAnimation = "66 3D 08 00 73 08 5F 33 C0 5E 83 C4 28 C3 66 3D 50 00 72";

    /// <summary>
    /// Pressing state: mov [esp+0x20],edi ; call (direction) ; add esp,0x10 ; cmp edi,0xA0 (at +12) ; jge fail ; push 0x50 ;
    /// push edi ; push esi ; call take-ball attempt.
    /// </summary>
    private const string TackleRange = "89 7C 24 20 E8 ?? ?? ?? ?? 83 C4 10 81 FF A0 00 00 00 7D ?? 6A 50 57 56 E8";

    /// <summary>
    /// Pass gauge to distance: mov al,[edi+0x4D] ; cmp al,0x80 ; jb ; mov eax,0x3800 ; jmp ; movzx eax,al (at +14) ; shr eax,3 ;
    /// imul eax,eax,0x380 ; add eax,0x800.
    /// </summary>
    private const string LoftedGauge = "8A 47 4D 3C 80 72 07 B8 00 38 00 00 EB 0C 0F B6 C0 C1 E8 03 69 C0 80 03 00 00 05 00 08 00 00";

    /// <summary>Receiver angle: cmp ax,0x38 ; jae ; cmp word [esp+0x1C],0x20 (the byte at +11) ; jae.</summary>
    private const string LoftedAngle = "66 3D 38 00 73 ?? 66 83 7C 24 1C 20 73";

    /// <summary>
    /// Pass point: mov eax,[edi+0xA8] ; push eax ; push ebx ; call cos ; mov ecx,[edi+0xA8] (at +13) ; mov ebp,[esp+0x20] ;
    /// push ecx ; add eax,ebp ; push ebx ; mov [edi+0x9C],eax ; call sin.
    /// </summary>
    private const string PassLead = "8B 87 A8 00 00 00 50 53 E8 ?? ?? ?? ?? 8B 8F A8 00 00 00 8B 6C 24 20 51 03 C5 53 89 87 9C 00 00 00 E8";

    public static PassLeadSites? LocatePassLead(byte[] exe)
    {
        var image = new PeImage(exe);
        var hits = image.Sections.Where(s => s.IsCode).SelectMany(s => new Signature(PassLead).FindAll(image, s)).ToList();
        return hits.Count == 1 ? new PassLeadSites(hits[0], hits[0] + 13) : null;
    }

    /// <summary>xor edx,edx ; mov dx,[esi+0x26] ; push 100h ; push edx ; push ebx ; call turn ; mov [esi+0x26],ax ; mov [esi+0x28],ax ; mov [esi+0x42],ax.</summary>
    private const string CarrierTurnFirst = "33 D2 66 8B 56 26 68 00 01 00 00 52 53 E8 ?? ?? ?? ?? 66 89 46 26 66 89 46 28 66 89 46 42";

    /// <summary>xor ecx,ecx ; cmp word [eax+0x30],0x16 ; mov al,[esi+0x63] ; setne cl ; dec ecx ; and ecx,0x80 ; add ecx,100h.</summary>
    private const string CarrierTurnSecond = "33 C9 66 83 78 30 16 8A 46 63 0F 95 C1 49 81 E1 80 00 00 00 81 C1 00 01 00 00";

    public static CarrierTurnSites? LocateCarrierTurn(byte[] exe)
    {
        var image = new PeImage(exe);
        List<int> Find(string pattern) => [.. image.Sections.Where(s => s.IsCode).SelectMany(s => new Signature(pattern).FindAll(image, s))];
        var first = Find(CarrierTurnFirst);
        var second = Find(CarrierTurnSecond);
        return first.Count == 1 && second.Count == 1 ? new CarrierTurnSites(first[0] + 7, second[0] + 22) : null;
    }

    /// <summary>One axis of the pad reader: neg eax (distance from 128) ; cmp eax,25h ; jle +12 ; movzx reg,dl ; sub reg,80h.</summary>
    private const string StickAxis = "F7 D8 83 F8 25 7E 0C 66 0F B6 ?? 81 ?? 80 00 00 00";

    /// <summary>Set piece aim: test al,al ; jns +7 ; add dword [esi+0xA0],0x40 ; test al,0x20 ; jz +7 ; add dword [esi+0xA0],-0x40.</summary>
    private const string SetPieceAim = "84 C0 79 07 83 86 A0 00 00 00 40 A8 20 74 07 83 86 A0 00 00 00 C0";

    public static StickDeadZoneSites? LocateStickDeadZone(byte[] exe)
    {
        var image = new PeImage(exe);
        var hits = image.Sections.Where(s => s.IsCode).SelectMany(s => new Signature(StickAxis).FindAll(image, s)).Order().ToList();
        // the 4 axes follow one another in the same function
        var aims = image.Sections.Where(s => s.IsCode).SelectMany(s => new Signature(SetPieceAim).FindAll(image, s)).Order().ToList();
        return hits.Count == 4 && hits[3] - hits[0] < 0x100 && aims.Count == 2
            ? new StickDeadZoneSites([.. hits.Select(h => h + 4)], [.. aims]) : null;
    }

    public static LoftedAimSites? LocateLoftedAim(byte[] exe)
    {
        var image = new PeImage(exe);
        List<int> Find(string pattern) => [.. image.Sections.Where(s => s.IsCode).SelectMany(s => new Signature(pattern).FindAll(image, s))];
        var gauge = Find(LoftedGauge);
        var angle = Find(LoftedAngle);
        return gauge.Count == 1 && angle.Count == 1 ? new LoftedAimSites(gauge[0] + 14, angle[0] + 11) : null;
    }

    public static TackleSites? LocateTackle(byte[] exe)
    {
        var image = new PeImage(exe);
        var hits = image.Sections.Where(s => s.IsCode).SelectMany(s => new Signature(TackleRange).FindAll(image, s)).ToList();
        if (hits.Count != 1) return null;
        int range = hits[0] + 12;
        return new TackleSites(range, range + 8 + (sbyte)image.Bytes[image.Offset(range + 7)]);
    }

    public static SprintTurnSites? LocateSprintTurn(byte[] exe)
    {
        var image = new PeImage(exe);
        List<int> Find(string pattern) => [.. image.Sections.Where(s => s.IsCode).SelectMany(s => new Signature(pattern).FindAll(image, s))];
        var limit = Find(SprintTurnLimit);
        var touch = Find(SprintTouch);
        var other = Find(SprintOtherTouch);
        var animation = Find(SprintTurnAnimation);
        return limit.Count == 1 && touch.Count == 1 && other.Count == 1 && animation.Count == 1
            ? new SprintTurnSites(limit[0] + 26, touch[0] + 58, other[0], image.ReadInt32(touch[0] + 1), animation[0] + 16)
            : null;
    }

    /// <summary>Player array: imul reg,reg,0x240 ; add reg,players (the most frequent base).</summary>
    public static SpreadSites? LocateSpread(byte[] exe)
    {
        var locator = new ExeLocator(new PeImage(exe));
        int entry = locator.Unique(TargetSetter, "Hook.Spread");
        var bases = new Dictionary<int, int>();
        foreach (int hit in locator.Hits("69 ?? 40 02 00 00"))
        {
            // add eax,imm32 or add reg,imm32 within the next 3 bytes
            var next = locator._image.Read(hit + 6, 9);
            for (int k = 0; k < 3; k++)
            {
                int va = next[k] == 0x05 ? BitConverter.ToInt32(next, k + 1)
                    : next[k] == 0x81 && (next[k + 1] & 0xF8) == 0xC0 ? BitConverter.ToInt32(next, k + 2) : 0;
                if (va > locator._image.ImageBase && va < locator._image.ImageBase + 0x10000000) { bases[va] = bases.GetValueOrDefault(va) + 1; break; }
            }
        }
        int players = bases.Count > 0 ? bases.MaxBy(kv => kv.Value).Key : 0;
        return entry != 0 && players != 0 ? new SpreadSites(entry, players) : null;
    }

    private IntroSites? FindIntro()
    {
        int movie = Unique(MovieSkip, "Hook.Intro");
        int logos = Unique(LogoSkip, "Hook.Intro");
        return movie == 0 || logos == 0 ? null : new IntroSites([movie + MovieSkipJump, .. LogoSkipJumps.Select(j => logos + j)]);
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
        int stick = Unique(StickTableReader, "Hook.FreeDirection");
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

        int keyState = _image.ImportSlot("USER32.dll", "GetAsyncKeyState");
        int moduleHandle = _image.ImportSlot("KERNEL32.dll", "GetModuleHandleA");
        int procAddress = _image.ImportSlot("KERNEL32.dll", "GetProcAddress");
        int tickCount = _image.ImportSlot("KERNEL32.dll", "GetTickCount");
        bool imports = keyState != 0 && moduleHandle != 0 && procAddress != 0 && tickCount != 0;
        if (!imports) Problem("Hook.Dribble", 0);
        var rdata = _image.Named(".rdata");
        int state = rdata != null && _image.Matches(rdata.End - 0x10, new byte[0x10]) ? rdata.End - 0x10 : 0;
        if (state == 0) Problem("Hook.Dribble", rdata?.End ?? 0);
        int reader = Unique(DribbleAccuracyReader, "Hook.Dribble");
        int accuracy = reader != 0 ? _image.ReadInt32(reader + 23) : 0;

        return run.Length == RunTouchSignatures.Length && sprint.Length == SprintTouchSignatures.Length && imports && state != 0 && accuracy != 0
            ? new DribbleSites(relay, run, sprint, run[SnappedTouchIndex])
            {
                KeyStateImport = keyState, ModuleHandleImport = moduleHandle, ProcAddressImport = procAddress,
                TickCountImport = tickCount, SwitchState = state, DribbleAccuracy = accuracy,
            }
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
