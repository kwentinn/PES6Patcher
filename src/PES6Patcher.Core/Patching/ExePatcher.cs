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
public sealed record PatchOptions(int Half, bool FreeDirection, bool Display = true, bool CloseDribble = false, bool SkipIntro = false, bool FreeKick = false,
    int FreeKickWeight = PatchOptions.DefaultFreeKickWeight, bool FreeKickDebug = false)
{
    /// <summary>Weight added to the ball of a free kick shot, in % of the game's weight: default (tuned in game) and limits.</summary>
    public const int DefaultFreeKickWeight = 50, MinFreeKickWeight = 10, MaxFreeKickWeight = 200;
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

    public bool AnyFix => Display || FreeDirection || CloseDribble || SkipIntro || FreeKick;
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
            var (display, direction, dribble, intro, freeKick, patched, problems) = ExeLocator.Locate(exe);
            return new ExeReport(hash, ValidatedSha256.Contains(hash), patched, display != null, direction != null, problems) { CloseDribble = dribble != null, SkipIntro = intro != null, FreeKick = freeKick != null };
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
        var (display, direction, dribble, intro, freeKick, patched, problems) = ExeLocator.Locate(original);
        if (patched) throw new InvalidOperationException(Loc.T("Error.AlreadyPatched"));
        if (options.Display && display == null) throw new PatchException(problems);
        if (options.FreeDirection && direction == null) throw new PatchException(problems);
        if (options.CloseDribble && dribble == null) throw new PatchException(problems);
        if (options.SkipIntro && intro == null) throw new PatchException(problems);
        if (options.FreeKick && freeKick == null) throw new PatchException(problems);

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
        if (dribbleDirection || options.CloseDribble)
        {
            int size = CodeCaves.KickRelaySize(dribbleDirection, options.CloseDribble);
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
            var hook = CodeCaves.KickRelay(relayCave, dribble!, dribbleDirection ? direction : null, options.CloseDribble, switchSetUp);
            if (hook.CaveBytes.Length != size) throw new InvalidOperationException("Added code size does not match its reserved space.");
            plan.Add((hook.Cave, hook.CaveBytes));
            plan.Add((hook.Entry, hook.EntryBytes));
        }

        // intro: the movie and logo skip paths are always taken (je -> nop nop)
        if (options.SkipIntro)
            foreach (int jump in intro!.Jumps) plan.Add((jump, [0x90, 0x90]));

        // free kicks: heavier ball after a free kick shot; its state lives in the end of .rdata
        if (options.FreeKick)
        {
            if (options.FreeKickWeight is < PatchOptions.MinFreeKickWeight or > PatchOptions.MaxFreeKickWeight)
                throw new ArgumentOutOfRangeException(nameof(options), options.FreeKickWeight, null);
            float unit = image.ReadFloat(freeKick!.Weight) / 100f;
            (int, int)? texts = null;
            if (options.FreeKickDebug)
            {
                // test key texts (0 to 200 %), in a cave of their own: too large to share one with the code
                var (blob, stride) = CodeCaves.FreeKickTexts([.. Enumerable.Range(0, PatchOptions.MaxFreeKickWeight / CodeCaves.FreeKickWeightStep + 1)
                    .Select(k => Ascii(Loc.T("Overlay.FreeKickWeight", k * CodeCaves.FreeKickWeightStep)))]);
                int textsAt = Cave(blob.Length);
                plan.Add((textsAt, blob));
                texts = (textsAt, stride);
            }
            int size = CodeCaves.FreeKick(0, freeKick, unit, options.FreeKickWeight, texts).Cave.Length;
            int at = Cave(size);
            var (cave, entries) = CodeCaves.FreeKick(at, freeKick, unit, options.FreeKickWeight, texts);
            plan.Add((at, cave));
            plan.AddRange(entries);
            image.MakeWritable(image.SectionOf(freeKick.State)!);
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
