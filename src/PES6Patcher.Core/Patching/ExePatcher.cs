using System.Security.Cryptography;

namespace PES6Patcher.Core.Patching;

/// <summary>Options of a patch build.</summary>
/// <param name="Half">Virtual half-width of the 2D layer: 224 * aspect (398 for 16:9, 796 for 32:9). 256 = original.</param>
/// <param name="FreeDirection">Run along the analog stick's precise angle instead of 8 directions.</param>
/// <param name="Display">Widescreen 2D layer (menus, HUD, list clipping, player cursor).</param>
/// <param name="CloseDribble">Shorter dribble touches: the player can change direction sooner.</param>
/// <param name="SkipIntro">Skip the opening movie and the logos (the game's own skip paths).</param>
/// <param name="FreeKick">Easier free kicks: a heavier ball after a free kick shot, so it dips sooner after the wall.</param>
/// <param name="FreeKickWeight">Weight added to that ball, in % of the game's ball weight.</param>
/// <param name="FreeKickDebug">Test key: F2 in game changes <paramref name="FreeKickWeight"/> by 10 % and shows it (through Kitserver).</param>
/// <param name="ShotPower">Shot power above the lowest one, in % of the game's (100 = unchanged): a short press sends the ball less high.</param>
/// <param name="ShotPowerDebug">Test key: F3 in game changes <paramref name="ShotPower"/> by 10 % and shows it (through Kitserver).</param>
/// <param name="ShotAccuracy">Human shots: share of the game's aim error (direction and height) kept, in % (100 = unchanged).</param>
/// <param name="ShotAccuracyDebug">Test key: F4 in game changes <paramref name="ShotAccuracy"/> by 10 % and shows it (through Kitserver).</param>
/// <param name="LobPower">With <paramref name="ShotAccuracy"/>, the speed of a lob (L1 held) in % of the game's; its height is kept (not taken for an error).</param>
/// <param name="LoftedAim">Lofted passes follow the press and the stick more: the distance wanted grows with the square of the gauge (same maximum), and the receiver must be within 22.5 degrees of the stick (45 in the game).</param>
/// <param name="PassLead">How far ahead of the receiver a pass is aimed, in % of the game's lead (100 = unchanged): through passes no longer run out of play.</param>
/// <param name="PassLeadDebug">Test key: F9 in game changes <paramref name="PassLead"/> by 10 % and shows it (through Kitserver).</param>
/// <param name="CarrierTurn">Running with the ball, the most the player's facing and run turn per frame, in degrees (0 = unchanged; the game's is 1.4): after a sharp touch, he follows the ball instead of running on along his old line.</param>
/// <param name="StickDeadZone">Analog stick dead zone of each axis, in % (<see cref="GameStickDeadZone"/> = unchanged): smaller, a stick held near an axis no longer snaps onto it.</param>
/// <param name="Spread">Players placing themselves spread across the pitch (width only), in % away from their team's centre (0 = unchanged).</param>
/// <param name="SpreadDebug">Test key: F5 in game changes <paramref name="Spread"/> by 10 % and shows it (through Kitserver).</param>
/// <param name="SpeedGapDebug">Test key: F6 in game changes <paramref name="SpeedGap"/> by 25 % and shows it (through Kitserver).</param>
/// <param name="SpeedGap">Top speed gap between players: effect of the top speed stat on the maximum running speed, in % of the game's (100 = unchanged).</param>
/// <param name="SprintTurn">Sharper turns when sprinting: most the ball may turn per sprint touch for a star dribbler, in degrees (0 = unchanged; the game's is 22.5, kept up to 65 in dribble accuracy).</param>
/// <param name="SprintTurnDebug">Test key: F7 in game changes <paramref name="SprintTurn"/> by 15 degrees and shows it (through Kitserver).</param>
/// <param name="Tackle">Pressing challenge reach: how far from the ball a pressing player tries to win it, in % of the game's (100 = unchanged).</param>
/// <param name="TackleDebug">Test key: F8 in game changes <paramref name="Tackle"/> by 20 % (back to 100 % after the maximum) and shows it (through Kitserver).</param>
/// <param name="AccelerationGap">Acceleration gap between players: the acceleration stat weighs twice as much on the time to reach a speed.</param>
/// <param name="CloseDribbleStrength">With <paramref name="CloseDribble"/>, how much the touches are shortened, in % of the first version's reduction.</param>
/// <param name="CloseDribbleStrengthDebug">Test key: F10 in game changes <paramref name="CloseDribbleStrength"/> by 25 % and shows it (through Kitserver).</param>
public sealed record PatchOptions(int Half, bool FreeDirection, bool Display = true, bool CloseDribble = false, bool SkipIntro = false, bool FreeKick = false,
    int FreeKickWeight = PatchOptions.DefaultFreeKickWeight, bool FreeKickDebug = false, int ShotPower = 100, bool ShotPowerDebug = false,
    int ShotAccuracy = 100, bool ShotAccuracyDebug = false, int Spread = 0, bool SpreadDebug = false, int SpeedGap = 100, bool SpeedGapDebug = false,
    int SprintTurn = 0, bool SprintTurnDebug = false, int Tackle = 100, bool TackleDebug = false,
    int LobPower = 100, bool LoftedAim = false, int PassLead = 100, bool PassLeadDebug = false, int StickDeadZone = PatchOptions.GameStickDeadZone, int CarrierTurn = 0,
    int CloseDribbleStrength = 100, bool CloseDribbleStrengthDebug = false, bool AccelerationGap = false)
{
    /// <summary>
    /// Close dribbling strength: how much of the touch reduction (x 14/16 running, x 13/16 sprinting from 95 in dribble
    /// accuracy) applies, in % (100 = the first version's touches for the best dribblers): default (tested in game) and limits.
    /// </summary>
    public const int DefaultCloseDribbleStrength = 125, MinCloseDribbleStrength = 50, MaxCloseDribbleStrength = 200;   // above 200 %, the touches are too short: the player treads on the ball

    /// <summary>
    /// Carrier turn rate, in degrees per frame (the game's: 1.4): default (22, about 16 times the game's: tested in game) and limits.
    /// </summary>
    public const int DefaultCarrierTurn = 22, MinCarrierTurn = 3, MaxCarrierTurn = 45;

    /// <summary>
    /// Analog stick dead zone of each axis, in % of the axis (the game's: 29 %, 37 of 127): default (tested in game) and limits.
    /// </summary>
    public const int GameStickDeadZone = 29, DefaultStickDeadZone = 18, MinStickDeadZone = 2, MaxStickDeadZone = 26;

    /// <summary>Pressing challenge reach, in % of the game's (100 = unchanged): default (tested in game) and limits.</summary>
    public const int DefaultTackle = 180, MinTackle = 100, MaxTackle = 180;

    /// <summary>Sprint turns: most a star dribbler's ball may turn per sprint touch, in degrees (the game's is 22.5): default and limits.</summary>
    public const int DefaultSprintTurn = 90, MinSprintTurn = 45, MaxSprintTurn = 90;

    /// <summary>Shot aim error kept: default when the option is checked, and limits (100 = the game's).</summary>
    public const int DefaultShotAccuracy = 50, MinShotAccuracy = 0, MaxShotAccuracy = 100;

    /// <summary>Lob speed with the shot accuracy, in % of the game's (100 = unchanged): default (tested in game) and limits.</summary>
    public const int DefaultLobPower = 70, MinLobPower = 40, MaxLobPower = 100;

    /// <summary>Pass lead, in % of the game's (100 = unchanged): default (tested in game) and limits.</summary>
    public const int DefaultPassLead = 60, MinPassLead = 30, MaxPassLead = 100;

    /// <summary>Weight added to the ball of a free kick shot, in % of the game's weight: default (tuned in game) and limits.</summary>
    public const int DefaultFreeKickWeight = 50, MinFreeKickWeight = 10, MaxFreeKickWeight = 200;

    /// <summary>Shot power: default when the option is checked, and limits (100 = the game's).</summary>
    public const int DefaultShotPower = 80, MinShotPower = 50, MaxShotPower = 100;

    /// <summary>Team spread across the pitch, in % away from the centre of the team: default and limits (tuned in game).</summary>
    public const int DefaultSpread = 20, MinSpread = 10, MaxSpread = 40;

    /// <summary>Top speed gap between players, in % of the game's (100 = unchanged): default (tuned in game) and limits.</summary>
    public const int DefaultSpeedGap = 300, MinSpeedGap = 100, MaxSpeedGap = 400;
}

/// <summary>What the patcher can do with a given PES6.exe.</summary>
/// <param name="Sha256">Hash of the file.</param>
/// <param name="Validated">This exact build was tested in game.</param>
/// <param name="AlreadyPatched">The file is already patched (2D layer not 512 units wide, lowered turn threshold or hooked kick relay).</param>
/// <param name="Display">The widescreen fix can be applied.</param>
/// <param name="FreeDirection">The free stick direction fix can be applied.</param>
/// <param name="Problems">Locations that could not be found.</param>
public sealed record ExeReport(string Sha256, bool Validated, bool AlreadyPatched, bool Display, bool FreeDirection, IReadOnlyList<string> Problems)
{
    /// <summary>The close-dribbling fix can be applied.</summary>
    public bool CloseDribble { get; init; }

    /// <summary>The intro can be skipped.</summary>
    public bool SkipIntro { get; init; }

    /// <summary>The free kick fix can be applied.</summary>
    public bool FreeKick { get; init; }

    /// <summary>The shot power can be reduced and the shot accuracy raised.</summary>
    public bool ShotPower { get; init; }

    /// <summary>The players can be spread across the pitch.</summary>
    public bool Spread { get; init; }

    /// <summary>The top speed gap between players can be widened.</summary>
    public bool SpeedGap { get; init; }

    /// <summary>The sprint turns can be made sharper.</summary>
    public bool SprintTurn { get; init; }

    /// <summary>The pressing challenge reach can be widened.</summary>
    public bool Tackle { get; init; }

    /// <summary>The lofted pass aim can be changed.</summary>
    public bool LoftedAim { get; init; }

    /// <summary>The pass lead can be shortened.</summary>
    public bool PassLead { get; init; }

    /// <summary>The analog stick dead zone can be made smaller.</summary>
    public bool StickDeadZone { get; init; }

    /// <summary>The carrier can turn faster with the ball.</summary>
    public bool CarrierTurn { get; init; }

    /// <summary>The acceleration gap between players can be widened.</summary>
    public bool AccelerationGap { get; init; }

    public bool AnyFix => Display || FreeDirection || CloseDribble || SkipIntro || FreeKick || ShotPower || Spread || SpeedGap || SprintTurn || Tackle || LoftedAim || PassLead || StickDeadZone || CarrierTurn || AccelerationGap;
}

public sealed class PatchException(IReadOnlyList<string> problems)
    : Exception(Loc.T("Error.UnsupportedExe", string.Join(" ; ", problems)))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// Builds a patched copy of a PES6.exe. Locations are found by signature (see <see cref="ExeLocator"/>),
/// so any build with the same game code can be patched; nothing is produced if a location is missing.
/// </summary>
public static class ExePatcher
{
    /// <summary>SHA-256 of the builds tested in game.</summary>
    public static readonly IReadOnlySet<string> ValidatedSha256 = new HashSet<string>
    {
        "3FE13850C933C85385F3FB5C45977D2DAA860D1FC03E95E97BE9F4FC24D81382",
    };

    public static string Sha256(byte[] exe) => Convert.ToHexString(SHA256.HashData(exe));

    /// <summary>Text for Kitserver's font: accents removed, other non-ASCII characters dropped.</summary>
    internal static string Ascii(string text) =>
        new([.. text.Normalize(System.Text.NormalizationForm.FormD).Where(c => c < 128)]);

    public static ExeReport Analyze(byte[] exe)
    {
        string hash = Sha256(exe);
        try
        {
            var (display, direction, dribble, intro, freeKick, shot, patched, problems) = ExeLocator.Locate(exe);
            return new ExeReport(hash, ValidatedSha256.Contains(hash), patched, display != null, direction != null, problems)
            {
                CloseDribble = dribble != null, SkipIntro = intro != null, FreeKick = freeKick != null, ShotPower = shot != null,
                Spread = ExeLocator.LocateSpread(exe) != null, SpeedGap = ExeLocator.LocateSpeed(exe) != null,
                SprintTurn = dribble != null && ExeLocator.LocateSprintTurn(exe) != null, Tackle = ExeLocator.LocateTackle(exe) != null, LoftedAim = ExeLocator.LocateLoftedAim(exe) != null,
                PassLead = ExeLocator.LocatePassLead(exe) != null, StickDeadZone = ExeLocator.LocateStickDeadZone(exe) != null,
                CarrierTurn = ExeLocator.LocateCarrierTurn(exe) != null, AccelerationGap = ExeLocator.LocateAcceleration(exe) != null,
            };
        }
        catch (InvalidDataException ex)
        {
            return new ExeReport(hash, false, false, false, false, [ex.Message]);
        }
    }

    /// <summary>An unpatched exe where at least one fix (display, free direction, close dribbling) can be applied.</summary>
    public static bool IsSupported(byte[] exe) => Analyze(exe) is { AlreadyPatched: false } r && r.AnyFix;

    /// <summary>Returns a patched copy of <paramref name="original"/>.</summary>
    public static byte[] Build(byte[] original, PatchOptions options)
    {
        if (options.Half is < 256 or > 2048) throw new ArgumentOutOfRangeException(nameof(options), Loc.T("Error.HalfOutOfRange"));

        var image = new PeImage((byte[])original.Clone());
        var (display, direction, dribble, intro, freeKick, shot, patched, problems) = ExeLocator.Locate(original);
        if (patched) throw new InvalidOperationException(Loc.T("Error.AlreadyPatched"));
        if (options.Display && display == null) throw new PatchException(problems);
        if (options.FreeDirection && direction == null) throw new PatchException(problems);
        if (options.CloseDribble && dribble == null) throw new PatchException(problems);
        if (options.SkipIntro && intro == null) throw new PatchException(problems);
        if (options.FreeKick && freeKick == null) throw new PatchException(problems);
        if ((options.ShotPower < 100 || options.ShotAccuracy < 100) && shot == null) throw new PatchException(problems);
        if (options.ShotPower is < PatchOptions.MinShotPower or > PatchOptions.MaxShotPower)
            throw new ArgumentOutOfRangeException(nameof(options), options.ShotPower, null);
        if (options.ShotAccuracy is < PatchOptions.MinShotAccuracy or > PatchOptions.MaxShotAccuracy)
            throw new ArgumentOutOfRangeException(nameof(options), options.ShotAccuracy, null);

        var plan = new List<(int Va, byte[] Bytes)>();
        var caves = new CaveAllocator(image);
        int Cave(int size) => caves.Allocate(size) is var at and not 0 ? at : throw new PatchException([Loc.T("Check.HookCave", "")]);

        if (options.Display) PlanDisplay(display!, options.Half, plan, Cave);

        // free stick direction, and a lower turn threshold (45 -> ~17 degrees) for the running state
        if (options.FreeDirection)
        {
            var hook = CodeCaves.FreeDirection(Cave(CodeCaves.FreeDirectionSize), direction!);
            if (hook.CaveBytes.Length != CodeCaves.FreeDirectionSize) throw new InvalidOperationException("Added code size does not match its reserved space.");
            plan.Add((hook.Cave, hook.CaveBytes));
            plan.Add((hook.Entry, hook.EntryBytes));
            plan.Add((direction!.TurnThreshold + 3, [0x0C]));
        }

        // ball kick relay (allocated last, so the other caves stay where they were): the free direction
        // also covers the dribble touch rounded to 22.5 degrees; close dribbling shortens the touches
        bool dribbleDirection = options.FreeDirection && dribble != null;
        bool strengthKey = options.CloseDribble && options.CloseDribbleStrengthDebug;
        if (options.CloseDribble && options.CloseDribbleStrength is < PatchOptions.MinCloseDribbleStrength or > PatchOptions.MaxCloseDribbleStrength)
            throw new ArgumentOutOfRangeException(nameof(options), options.CloseDribbleStrength, null);
        if (strengthKey && freeKick == null) throw new PatchException(problems);
        if (dribbleDirection || options.CloseDribble)
        {
            int size = CodeCaves.KickRelaySize(dribbleDirection, options.CloseDribble, strengthKey);
            int relayCave = Cave(size);
            int switchSetUp = 0;
            if (options.CloseDribble)
            {
                // F1 switch with a Kitserver message; its state lives in the last 16 bytes of .rdata
                string on = Ascii(Loc.T("Overlay.CloseDribbleOn")), off = Ascii(Loc.T("Overlay.CloseDribbleOff"));
                int messageSize = CodeCaves.SwitchMessage(0, dribble!, on, off).Length;
                switchSetUp = Cave(messageSize);
                plan.Add((switchSetUp, CodeCaves.SwitchMessage(switchSetUp, dribble!, on, off)));
                image.MakeWritable(image.SectionOf(dribble!.SwitchState)!);
            }
            var hook = CodeCaves.KickRelay(relayCave, dribble!, dribbleDirection ? direction : null, options.CloseDribble, switchSetUp,
                options.CloseDribbleStrength, strengthKey ? CodeCaves.CloseDribbleStrengthAddress(freeKick!) : 0);
            if (hook.CaveBytes.Length != size) throw new InvalidOperationException("Added code size does not match its reserved space.");
            plan.Add((hook.Cave, hook.CaveBytes));
            plan.Add((hook.Entry, hook.EntryBytes));
        }

        // intro: the movie and logo skip paths are always taken (je -> nop nop)
        if (options.SkipIntro)
            foreach (int jump in intro!.Jumps) plan.Add((jump, [0x90, 0x90]));

        // test keys (F2: free kick weight, F3: shot power, F4: shot aim error), shown through Kitserver; their texts sit
        // in caves of their own (too large to share one with the code), their state with the free kick one
        bool freeKickKey = options.FreeKick && options.FreeKickDebug, shotKey = options.ShotPower < 100 && options.ShotPowerDebug,
            accuracyKey = options.ShotAccuracy < 100 && options.ShotAccuracyDebug, spreadKey = options.Spread > 0 && options.SpreadDebug,
            speedKey = options.SpeedGap != 100 && options.SpeedGapDebug, sprintKey = options.SprintTurn > 0 && options.SprintTurnDebug,
            tackleKey = options.Tackle != 100 && options.TackleDebug,
            passLeadKey = options.PassLead < 100 && options.PassLeadDebug;
        if ((freeKickKey || shotKey || accuracyKey || spreadKey || speedKey || sprintKey || tackleKey || passLeadKey || strengthKey) && freeKick == null) throw new PatchException(problems);
        var keys = new List<CodeCaves.TestKey>();
        (int Texts, int Stride) KeyTexts(int max, string key, int step = CodeCaves.TestKeyStep)
        {
            var (blob, stride) = CodeCaves.TestKeyTexts([.. Enumerable.Range(0, max / step + 1)
                .Select(k => Ascii(Loc.T(key, k * step)))]);
            int at = Cave(blob.Length);
            plan.Add((at, blob));
            return (at, stride);
        }
        if (freeKickKey)
        {
            var (texts, stride) = KeyTexts(PatchOptions.MaxFreeKickWeight, "Overlay.FreeKickWeight");
            keys.Add(CodeCaves.FreeKickWeightTestKey(options.FreeKickWeight, texts, stride));
        }
        if (shotKey)
        {
            var (texts, stride) = KeyTexts(PatchOptions.MaxShotPower, "Overlay.ShotPower");
            keys.Add(CodeCaves.ShotPowerTestKey(options.ShotPower, texts, stride));
        }
        if (accuracyKey)
        {
            var (texts, stride) = KeyTexts(PatchOptions.MaxShotAccuracy, "Overlay.ShotAccuracy");
            keys.Add(CodeCaves.ShotAccuracyTestKey(options.ShotAccuracy, texts, stride));
        }
        if (spreadKey)
        {
            var (texts, stride) = KeyTexts(PatchOptions.MaxSpread, "Overlay.Spread");
            keys.Add(CodeCaves.SpreadTestKey(options.Spread, texts, stride));
        }
        if (speedKey)
        {
            var (texts, stride) = KeyTexts(PatchOptions.MaxSpeedGap, "Overlay.SpeedGap", CodeCaves.SpeedGapStep);
            keys.Add(CodeCaves.SpeedGapTestKey(options.SpeedGap, texts, stride));
        }
        if (sprintKey)
        {
            var (texts, stride) = KeyTexts(PatchOptions.MaxSprintTurn, "Overlay.SprintTurn", CodeCaves.SprintTurnStep);
            keys.Add(CodeCaves.SprintTurnTestKey(options.SprintTurn, texts, stride));
        }
        if (tackleKey)
        {
            var (texts, stride) = KeyTexts(PatchOptions.MaxTackle, "Overlay.Tackle", CodeCaves.TackleStep);
            keys.Add(CodeCaves.TackleTestKey(options.Tackle, texts, stride));
        }
        if (passLeadKey)
        {
            var (texts, stride) = KeyTexts(PatchOptions.MaxPassLead, "Overlay.PassLead");
            keys.Add(CodeCaves.PassLeadTestKey(options.PassLead, texts, stride));
        }
        if (strengthKey)
        {
            var (texts, stride) = KeyTexts(PatchOptions.MaxCloseDribbleStrength, "Overlay.CloseDribbleStrength", CodeCaves.CloseDribbleStrengthStep);
            keys.Add(CodeCaves.CloseDribbleStrengthTestKey(options.CloseDribbleStrength, texts, stride));
        }
        int testKeysSetUp = 0;
        if (keys.Count > 0)
        {
            testKeysSetUp = Cave(CodeCaves.TestKeys(0, freeKick!, keys).Length);
            plan.Add((testKeysSetUp, CodeCaves.TestKeys(testKeysSetUp, freeKick!, keys)));
            image.MakeWritable(image.SectionOf(freeKick!.State)!);
        }

        // free kicks: heavier ball after a free kick shot; its state lives in the end of .rdata
        bool shots = options.ShotPower < 100 || options.ShotAccuracy < 100;
        var humans = shots ? shot : null;   // human teams learnt at the kick function's entry
        int freeKickShotReturn = 0;
        if (options.FreeKick)
        {
            if (options.FreeKickWeight is < PatchOptions.MinFreeKickWeight or > PatchOptions.MaxFreeKickWeight)
                throw new ArgumentOutOfRangeException(nameof(options), options.FreeKickWeight, null);
            float unit = image.ReadFloat(freeKick!.Weight) / 100f;
            int size = CodeCaves.FreeKick(0, freeKick, unit, options.FreeKickWeight, testKeysSetUp, humans).Cave.Length;
            int at = Cave(size);
            var (cave, entries, shotReturn) = CodeCaves.FreeKick(at, freeKick, unit, options.FreeKickWeight, testKeysSetUp, humans);
            plan.Add((at, cave));
            plan.AddRange(entries);
            image.MakeWritable(image.SectionOf(freeKick.State)!);
            freeKickShotReturn = shotReturn;
        }
        else if (keys.Count > 0 || humans != null)
        {
            // without the free kick hook at the kick function's entry, the test keys and human teams get one of their own
            int kickEntry = humans?.KickEntry ?? freeKick!.KickEntry;
            var testKeys = keys.Count > 0 ? freeKick : null;
            var hook = CodeCaves.KickEntryExtras(0, kickEntry, testKeys, testKeysSetUp, humans);
            hook = CodeCaves.KickEntryExtras(Cave(hook.CaveBytes.Length), kickEntry, testKeys, testKeysSetUp, humans);
            plan.Add((hook.Cave, hook.CaveBytes));
            plan.Add((hook.Entry, hook.EntryBytes));
        }

        // shot power: scaled at the shot function's entry, free kicks excepted (the entry also keeps the direction
        // asked, for the accuracy)
        if (shots)
        {
            var testKeys = shotKey ? freeKick : null;
            int size = CodeCaves.ShotPower(0, shot!, options.ShotPower, freeKickShotReturn, testKeys).CaveBytes.Length;
            var hook = CodeCaves.ShotPower(Cave(size), shot!, options.ShotPower, freeKickShotReturn, testKeys);
            plan.Add((hook.Cave, hook.CaveBytes));
            plan.Add((hook.Entry, hook.EntryBytes));
            image.MakeWritable(image.SectionOf(shot!.HumanScores)!);
        }
        // shot accuracy: the random gap in direction and height reduced, for human teams, set pieces excepted
        if (options.ShotAccuracy < 100)
        {
            var testKeys = accuracyKey ? freeKick : null;
            if (options.LobPower is < PatchOptions.MinLobPower or > PatchOptions.MaxLobPower) throw new ArgumentOutOfRangeException(nameof(options), options.LobPower, null);
            var accuracy = CodeCaves.ShotAccuracy(0, shot!, options.ShotAccuracy, testKeys, options.LobPower);
            accuracy = CodeCaves.ShotAccuracy(Cave(accuracy.CaveBytes.Length), shot!, options.ShotAccuracy, testKeys, options.LobPower);
            plan.Add((accuracy.Cave, accuracy.CaveBytes));
            plan.Add((accuracy.Entry, accuracy.EntryBytes));
        }

        // team spread: targets of the players placing themselves moved away from their team's centre, across the pitch
        if (options.Spread > 0)
        {
            if (options.Spread is < PatchOptions.MinSpread or > PatchOptions.MaxSpread) throw new ArgumentOutOfRangeException(nameof(options), options.Spread, null);
            var sites = ExeLocator.LocateSpread(original) ?? throw new PatchException([Loc.T("Check.Location", Loc.T("Hook.Spread"), 0)]);
            var testKeys = spreadKey ? freeKick : null;
            var spread = CodeCaves.Spread(0, sites, options.Spread, testKeys);
            spread = CodeCaves.Spread(Cave(spread.CaveBytes.Length), sites, options.Spread, testKeys);
            plan.Add((spread.Cave, spread.CaveBytes));
            plan.Add((spread.Entry, spread.EntryBytes));
        }

        // top speed gap: each player's maximum running speed follows the top speed stat more
        if (options.SpeedGap != 100)
        {
            if (options.SpeedGap is < PatchOptions.MinSpeedGap or > PatchOptions.MaxSpeedGap) throw new ArgumentOutOfRangeException(nameof(options), options.SpeedGap, null);
            var sites = ExeLocator.LocateSpeed(original) ?? throw new PatchException([Loc.T("Check.Location", Loc.T("Hook.Speed"), 0)]);
            var testKeys = speedKey ? freeKick : null;
            var speed = CodeCaves.Speed(0, sites, options.SpeedGap, testKeys);
            speed = CodeCaves.Speed(Cave(speed.CaveBytes.Length), sites, options.SpeedGap, testKeys);
            plan.Add((speed.Cave, speed.CaveBytes));
            plan.Add((speed.Entry, speed.EntryBytes));
        }

        // sprint turns: the ball may turn further per sprint touch for good dribblers, the player leaves with it at
        // either foot's touch, and from 45 degrees the turning animation plays
        if (options.SprintTurn > 0)
        {
            if (options.SprintTurn is < PatchOptions.MinSprintTurn or > PatchOptions.MaxSprintTurn)
                throw new ArgumentOutOfRangeException(nameof(options), options.SprintTurn, null);
            var sites = (dribble != null ? ExeLocator.LocateSprintTurn(original) : null)
                ?? throw new PatchException([Loc.T("Check.Location", Loc.T("Hook.SprintTurn"), 0)]);
            var testKeys = sprintKey ? freeKick : null;
            var limit = CodeCaves.SprintTurnLimit(0, sites, dribble!.DribbleAccuracy, options.SprintTurn, testKeys);
            limit = CodeCaves.SprintTurnLimit(Cave(limit.CaveBytes.Length), sites, dribble.DribbleAccuracy, options.SprintTurn, testKeys);
            Hook Touch(int site, byte[] replaced)
            {
                var size = CodeCaves.SprintTouch(0, site, replaced, sites.Ball).CaveBytes.Length;
                return CodeCaves.SprintTouch(Cave(size), site, replaced, sites.Ball);
            }
            var touch = Touch(sites.Touch, CodeCaves.SprintTouchBytes);
            var other = Touch(sites.OtherTouch, CodeCaves.SprintOtherTouchBytes);
            foreach (var hook in new[] { limit, touch, other }) { plan.Add((hook.Cave, hook.CaveBytes)); plan.Add((hook.Entry, hook.EntryBytes)); }
            plan.Add((sites.Animation, [CodeCaves.SprintTurnAnimationAngle]));
        }

        // pressing challenge: a pressing player tries to win the ball from further (the reach test and the attempt's
        // own distance limit); with F8, a hook reads the percent at run time
        if (options.Tackle != 100)
        {
            if (options.Tackle is < PatchOptions.MinTackle or > PatchOptions.MaxTackle) throw new ArgumentOutOfRangeException(nameof(options), options.Tackle, null);
            var sites = ExeLocator.LocateTackle(original) ?? throw new PatchException([Loc.T("Check.Location", Loc.T("Hook.Tackle"), 0)]);
            if (tackleKey)
            {
                var tackle = CodeCaves.Tackle(0, sites, options.Tackle, freeKick!);
                tackle = CodeCaves.Tackle(Cave(tackle.CaveBytes.Length), sites, options.Tackle, freeKick!);
                plan.Add((tackle.Cave, tackle.CaveBytes));
                plan.Add((tackle.Entry, tackle.EntryBytes));
            }
            else
            {
                var (range, parameter) = CodeCaves.TackleValues(options.Tackle);
                plan.Add((sites.Range + 2, BitConverter.GetBytes(range)));
                plan.Add((sites.Range + 9, [parameter]));
            }
        }

        // lofted pass aim: the distance wanted follows the square of the gauge (a short press aims nearer, the same
        // maximum), and the receiver is looked for within 22.5 degrees of the stick instead of 45
        if (options.LoftedAim)
        {
            var sites = ExeLocator.LocateLoftedAim(original) ?? throw new PatchException([Loc.T("Check.Location", Loc.T("Hook.LoftedAim"), 0)]);
            plan.Add((sites.Gauge, CodeCaves.LoftedGaugeSquare));
            plan.Add((sites.Angle, [CodeCaves.LoftedAngle]));
        }

        // carrier turn rate: running with the ball, the player turns faster towards the ball (after a sharp touch, he no
        // longer runs on along his old line)
        if (options.CarrierTurn > 0)
        {
            if (options.CarrierTurn is < PatchOptions.MinCarrierTurn or > PatchOptions.MaxCarrierTurn) throw new ArgumentOutOfRangeException(nameof(options), options.CarrierTurn, null);
            var sites = ExeLocator.LocateCarrierTurn(original) ?? throw new PatchException([Loc.T("Check.Location", Loc.T("Hook.CarrierTurn"), 0)]);
            byte[] step = BitConverter.GetBytes(CodeCaves.CarrierTurnStep(options.CarrierTurn));
            plan.Add((sites.First, step));
            plan.Add((sites.Second, step));
        }

        // acceleration gap: the acceleration stat's part of every acceleration time is divided by 5 instead of 10 (x 2; x 4
        // made the times of the best players negative: they crossed the pitch at once)
        if (options.AccelerationGap)
        {
            var sites = ExeLocator.LocateAcceleration(original) ?? throw new PatchException([Loc.T("Check.Location", Loc.T("Hook.AccelerationGap"), 0)]);
            foreach (int shift in sites.Shifts) plan.Add((shift, [CodeCaves.AccelerationGapShift]));
        }

        // analog stick dead zone: each axis is zeroed near the centre; a smaller zone lets a stick held near an axis
        // steer instead of snapping onto it
        if (options.StickDeadZone != PatchOptions.GameStickDeadZone)
        {
            if (options.StickDeadZone is < PatchOptions.MinStickDeadZone or > PatchOptions.MaxStickDeadZone) throw new ArgumentOutOfRangeException(nameof(options), options.StickDeadZone, null);
            var sites = ExeLocator.LocateStickDeadZone(original) ?? throw new PatchException([Loc.T("Check.Location", Loc.T("Hook.StickDeadZone"), 0)]);
            byte limit = CodeCaves.StickDeadZoneLimit(options.StickDeadZone);
            foreach (int site in sites.Limits) plan.Add((site, [limit]));
            // with the smaller zone, a slight push sent the corner, free kick and goal kick aims turning at full speed
            foreach (var (site, goalKick) in sites.Aims.Select(s => (s, false)).Concat(sites.GoalKickAims.Select(s => (s, true))))
            {
                var aim = CodeCaves.SetPieceAim(Cave(CodeCaves.SetPieceAim(0, site, goalKick).CaveBytes.Length), site, goalKick);
                plan.Add((aim.Cave, aim.CaveBytes));
                plan.Add((aim.Entry, aim.EntryBytes));
            }
            // the same for the training free kick ball placement
            var placement = CodeCaves.BallPlacement(Cave(CodeCaves.BallPlacement(0, sites.Placement, sites.StickTable).CaveBytes.Length), sites.Placement, sites.StickTable);
            plan.Add((placement.Cave, placement.CaveBytes));
            plan.Add((placement.Entry, placement.EntryBytes));
            plan.Add((sites.PlusX, CodeCaves.BallPlacementPlus(image.ReadInt32(sites.PlusX + 3))));
            plan.Add((sites.PlusZ, CodeCaves.BallPlacementPlus(image.ReadInt32(sites.PlusZ + 3))));
        }

        // pass lead: the pass point is placed less far ahead of the receiver (through passes stay in play)
        if (options.PassLead < 100)
        {
            if (options.PassLead < PatchOptions.MinPassLead) throw new ArgumentOutOfRangeException(nameof(options), options.PassLead, null);
            var sites = ExeLocator.LocatePassLead(original) ?? throw new PatchException([Loc.T("Check.Location", Loc.T("Hook.PassLead"), 0)]);
            var testKeys = passLeadKey ? freeKick : null;
            int size = CodeCaves.PassLead(0, sites, options.PassLead, testKeys).Cave.Length;
            int at = Cave(size);
            var (cave, entries) = CodeCaves.PassLead(at, sites, options.PassLead, testKeys);
            plan.Add((at, cave));
            plan.AddRange(entries);
        }

        foreach (var (va, bytes) in plan) image.Write(va, bytes);
        return image.Bytes;
    }

    /// <summary>Widescreen 2D layer. Its caves are allocated first, so the output matches the builds validated in game.</summary>
    private static void PlanDisplay(DisplaySites display, int half, List<(int Va, byte[] Bytes)> plan, Func<int, int> Cave)
    {
        // 1. private constants: half-width, 1/width, cursor scale
        int scale = display.ConstantsArea + 8;
        plan.Add((display.ConstantsArea, BitConverter.GetBytes((float)half)));
        plan.Add((display.ConstantsArea + 4, BitConverter.GetBytes(1f / (2 * half))));
        plan.Add((scale, BitConverter.GetBytes((2 * half) / 512f)));

        // 2. 2D layer width: the getter returns 2 * half (layout and clipping), but the two calls
        //    feeding it to the 3D projection get 512 (mov eax,0x200: same 5 bytes as the call)
        plan.Add((display.WidthGetter + 1, BitConverter.GetBytes(2 * half)));
        foreach (int call in display.Width3DCalls) plan.Add((call, [0xB8, 0x00, 0x02, 0x00, 0x00]));

        // clipping of scrolling lists, player cursor
        var clip = CodeCaves.Clip(half, Cave(CodeCaves.ClipSize), display.ClipEntry, display.ScreenWidthGetter);
        var cursor = CodeCaves.Cursor(Cave(CodeCaves.CursorSize), display.CursorEntry, scale);
        foreach (var hook in new[] { clip, cursor }) { plan.Add((hook.Cave, hook.CaveBytes)); plan.Add((hook.Entry, hook.EntryBytes)); }

        // 3. 2D conversions read the private constants (gameplay readers of 256.0 are left alone)
        foreach (int ins in display.HalfReaders) plan.Add((ins + 2, BitConverter.GetBytes(display.ConstantsArea)));
        foreach (int ins in display.InverseReaders) plan.Add((ins + 2, BitConverter.GetBytes(display.ConstantsArea + 4)));

        // 4. initial 2D view rectangles (x, y, w, h)
        foreach (int r in new[] { display.ViewRects, display.ViewRects + 16 })
        {
            plan.Add((r, BitConverter.GetBytes((float)-half)));
            plan.Add((r + 8, BitConverter.GetBytes((float)(2 * half))));
        }

        if (clip.CaveBytes.Length != CodeCaves.ClipSize || cursor.CaveBytes.Length != CodeCaves.CursorSize)
            throw new InvalidOperationException("Added code size does not match its reserved space.");
    }
}
