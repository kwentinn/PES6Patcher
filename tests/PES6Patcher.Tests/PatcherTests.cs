using PES6Patcher.Core;
using PES6Patcher.Core.Config;
using PES6Patcher.Core.Patching;
using Xunit.Abstractions;

namespace PES6Patcher.Tests;

/// <summary>
/// Compares the patcher's output with the files validated in game. Those files come from the
/// game and are not in the repository (research/reference, git-ignored); tests that need them
/// pass without checking anything when they are absent.
/// </summary>
public class PatcherTests(ITestOutputHelper output)
{
    private static readonly string? Reference = FindReference();

    private static string? FindReference()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "research", "reference");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private byte[]? Load(string relative)
    {
        var path = Reference == null ? null : Path.Combine(Reference, relative);
        if (path != null && File.Exists(path)) return File.ReadAllBytes(path);
        output.WriteLine($"Fichier de référence absent, test ignoré : {relative}");
        return null;
    }

    [Theory]
    [InlineData(398, "PES6_v6_16x9.exe")]
    [InlineData(796, "PES6_v6_32x9.exe")]
    public void Build_matches_the_exe_validated_in_game(int half, string validated)
    {
        var original = Load("PES6.orig.exe");
        var expected = Load(validated);
        if (original == null || expected == null) return;

        Assert.True(ExePatcher.IsSupported(original));
        var built = ExePatcher.Build(original, new PatchOptions(half, FreeDirection: true));

        // since 1.5, the free direction also hooks the kick relay (dribble touches): the rest is v6
        AssertSameExceptKickRelay(expected, built, CodeCaves.KickRelaySize(direction: true, closeDribble: false));
    }

    /// <summary>
    /// <paramref name="built"/> equals <paramref name="expected"/> except the kick relay entry (jmp cave ; nop),
    /// its cave of <paramref name="caveSize"/> bytes and, with close dribbling, the switch message cave
    /// (in 0xCC padding) and the .rdata write permission (F1 switch). Returns the relay cave's file offset.
    /// </summary>
    private static int AssertSameExceptKickRelay(byte[] expected, byte[] built, int caveSize)
    {
        var image = new PeImage(built);
        int relay = image.Offset(KickRelay);
        Assert.Equal(0xE9, built[relay]);
        Assert.Equal(0x90, built[relay + 5]);
        int cave = image.Offset(KickRelay + 5 + BitConverter.ToInt32(built, relay + 1));
        int rdataWrite = RdataFlagsOffset(built) + 3;   // high byte of the characteristics
        var diff = Enumerable.Range(0, built.Length)
            .Where(i => built[i] != expected[i] && !(i >= relay && i < relay + 6) && !(i >= cave && i < cave + caveSize) && i != rdataWrite
                && expected[i] != 0xCC)
            .Take(10).ToList();
        Assert.True(diff.Count == 0, "Octets différents aux offsets : " + string.Join(", ", diff.Select(i => $"0x{i:X}")));
        return cave;
    }

    /// <summary>File offset of the .rdata section header's characteristics.</summary>
    private static int RdataFlagsOffset(byte[] exe)
    {
        int pe = BitConverter.ToInt32(exe, 0x3C), table = pe + 24 + BitConverter.ToUInt16(exe, pe + 20);
        for (int s = table; ; s += 40)
            if (System.Text.Encoding.ASCII.GetString(exe, s, 6) == ".rdata") return s + 36;
    }

    private const int KickRelay = 0x4781F0;
    private static readonly int[] RunTouches = [0x5AF6D3, 0x5AF0CF, 0x5ADEC6, 0x5B0D65, 0x5B07A5];
    private static readonly int[] SprintTouches = [0x5AFB40, 0x5AFE6D];

    [Fact]
    public void Build_without_free_direction_leaves_the_direction_code_untouched()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        var built = ExePatcher.Build(original, new PatchOptions(398, FreeDirection: false));
        var image = new PeImage(original);
        int entry = image.Offset(0x4B27E0), threshold = image.Offset(0x5AF3CF);
        Assert.Equal(original.AsSpan(entry, 7).ToArray(), built.AsSpan(entry, 7).ToArray());
        Assert.Equal(original[threshold], built[threshold]);
    }

    [Fact]
    public void Gameplay_only_build_leaves_the_display_code_untouched_and_is_detected_as_patched()
    {
        var original = Load("PES6.orig.exe");
        var full = Load("PES6_v6_16x9.exe");
        if (original == null || full == null) return;

        var built = ExePatcher.Build(original, new PatchOptions(398, FreeDirection: true, Display: false));
        var image = new PeImage(original);
        int widthGetter = image.Offset(0x8744F0), entry = image.Offset(0x4B27E0), threshold = image.Offset(0x5AF3CF);
        Assert.Equal(original.AsSpan(widthGetter, 6).ToArray(), built.AsSpan(widthGetter, 6).ToArray());
        Assert.NotEqual(original.AsSpan(entry, 7).ToArray(), built.AsSpan(entry, 7).ToArray());
        Assert.Equal(0x0C, built[threshold]);

        var report = ExePatcher.Analyze(built);
        Assert.True(report.AlreadyPatched);
        Assert.False(ExePatcher.IsSupported(built));
        Assert.Throws<InvalidOperationException>(() => ExePatcher.Build(built, new PatchOptions(398, true)));
    }

    [Fact]
    public void Free_direction_is_available_even_when_the_display_fix_is_not()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        var altered = (byte[])original.Clone();
        altered[new PeImage(original).Offset(0x40D740)] ^= 0xFF;   // clip function entry
        var report = ExePatcher.Analyze(altered);
        Assert.False(report.Display);
        Assert.True(report.FreeDirection);
        Assert.True(ExePatcher.IsSupported(altered));
        ExePatcher.Build(altered, new PatchOptions(398, FreeDirection: true, Display: false));
    }

    [Fact]
    public void Close_dribble_hooks_only_the_kick_relay_and_adds_its_cave()
    {
        var original = Load("PES6.orig.exe");
        var v6 = Load("PES6_v6_16x9.exe");
        if (original == null || v6 == null) return;

        Assert.True(ExePatcher.Analyze(original).CloseDribble);
        var built = ExePatcher.Build(original, new PatchOptions(398, FreeDirection: true, CloseDribble: true));

        // everything else is the v6 build; after the direction part, the cave compares the return
        // address with the 7 dribble touches
        int cave = AssertSameExceptKickRelay(v6, built, CodeCaves.KickRelaySize(direction: true, closeDribble: true));
        int[] touches = [.. RunTouches, .. SprintTouches];
        for (int k = 0; k < touches.Length; k++)
        {
            int at = cave + CodeCaves.RelayDirectionSize + CloseDribbleSwitchSize + 3 + 7 * k;
            Assert.Equal(0x3D, built[at]);
            Assert.Equal(touches[k], BitConverter.ToInt32(built, at + 1));
        }

        // the reduction reads the player's dribble accuracy (0x3BCF5C0 in the reference build)
        byte[] read = [0x8B, 0x80, .. BitConverter.GetBytes(0x3BCF5C0), 0x83, 0xE0, 0x7F];
        int size = CodeCaves.KickRelaySize(direction: true, closeDribble: true);
        Assert.True(built.AsSpan(cave, size).IndexOf(read) >= 0);

        var report = ExePatcher.Analyze(built);
        Assert.True(report.AlreadyPatched);
    }

    [Fact]
    public void Close_dribble_alone_is_detected_as_patched()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        var built = ExePatcher.Build(original, new PatchOptions(398, FreeDirection: false, Display: false, CloseDribble: true));
        Assert.True(ExePatcher.Analyze(built).AlreadyPatched);
        Assert.False(ExePatcher.IsSupported(built));
    }

    [Fact]
    public void Free_kick_hooks_the_set_piece_shot_the_kick_entry_and_the_ball_weight()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        Assert.True(ExePatcher.Analyze(original).FreeKick);
        var options = new PatchOptions(398, FreeDirection: true, CloseDribble: true, SkipIntro: true);
        var without = ExePatcher.Build(original, options);
        var with = ExePatcher.Build(original, options with { FreeKick = true });

        // measured in game: call shot at 0x58CC11, kick function at 0x478020, fadd [weight] at 0x475EEF
        var image = new PeImage(with);
        Assert.Equal(0xE9, with[image.Offset(0x58CC11)]);
        int cave = image.Rel32Target(0x58CC11);
        Assert.Equal(cave + 56, image.Rel32Target(0x478020));
        Assert.Equal(cave + 82, image.Rel32Target(0x475EEF));
        Assert.Equal(0x5B9130, image.Rel32Target(cave + 39));                // the cave still calls the shot, after reading the sticks
        Assert.Equal(1.88f, image.ReadFloat(cave + FreeKickCaveSize - 4));   // 1 % of 188
        Assert.Equal(0x1016680 + 2, image.ReadInt32(cave + 6));              // pad magnitudes of the stick table
        Assert.Equal(0x10, with[image.Offset(cave + 20)]);                  // stick pushed up
        Assert.Equal(50, PercentInCave(with, image, cave));
        var heavier = ExePatcher.Build(original, options with { FreeKick = true, FreeKickWeight = 100 });
        Assert.Equal(100, PercentInCave(heavier, new PeImage(heavier), cave));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExePatcher.Build(original, options with { FreeKick = true, FreeKickWeight = 500 }));

        // only those 3 entries (5 + 7 + 6 bytes) and the cave change (.rdata is already writable for close dribbling)
        var changed = Enumerable.Range(0, with.Length).Where(i => with[i] != without[i]).ToList();
        int[] entries = [.. Range(image, 0x58CC11, 5), .. Range(image, 0x478020, 7), .. Range(image, 0x475EEF, 6)];
        Assert.DoesNotContain(changed.Except(entries), i => i < image.Offset(cave) || i >= image.Offset(cave) + FreeKickCaveSize);

        // test keys: the first kick sets up a Kitserver hook (F2: texts for 0 to 200 %, F3 and F4: 0 to 100 %)
        var debug = ExePatcher.Build(original, options with
        {
            FreeKick = true, FreeKickDebug = true, ShotPower = 80, ShotPowerDebug = true, ShotAccuracy = 50, ShotAccuracyDebug = true,
        });
        var debugImage = new PeImage(debug);
        int debugCave = debugImage.Rel32Target(0x58CC11);   // after the texts and test keys caves
        Assert.Equal(debugCave + 56, debugImage.Rel32Target(0x478020));
        Assert.Equal(debugCave + 228, debugImage.Rel32Target(0x475EEF));   // the kick part also learns the human teams
        int setUp = debugImage.Rel32Target(debugCave + 56 + 10);
        var text = System.Text.Encoding.ASCII.GetString(debug, debugImage.Offset(setUp), 600);
        Assert.Contains("kload.dll\0KDrawText\0HookFunction\0", text);
        var keysCode = debug.AsSpan(debugImage.Offset(setUp), 600);
        int firstTexts = keysCode.IndexOf(new byte[] { 0x69, 0xC0 }), secondTexts = firstTexts + 6 + keysCode[(firstTexts + 6)..].IndexOf(new byte[] { 0x69, 0xC0 });
        string Texts(int imul) => System.Text.Encoding.ASCII.GetString(debug, debugImage.Offset(debugImage.ReadInt32(setUp + imul + 7)), 1000);
        Assert.Contains(ExePatcher.Ascii(Loc.T("Overlay.FreeKickWeight", 200)), Texts(firstTexts));
        Assert.Contains(ExePatcher.Ascii(Loc.T("Overlay.ShotPower", 100)), Texts(secondTexts));
        int thirdTexts = secondTexts + 6 + keysCode[(secondTexts + 6)..].IndexOf(new byte[] { 0x69, 0xC0 });
        Assert.Contains(ExePatcher.Ascii(Loc.T("Overlay.ShotAccuracy", 100)), Texts(thirdTexts));

        // shot power: the shot function's entry jumps to code that scales the power, F3's percent first
        int shotCave = debugImage.Rel32Target(0x5B9130);
        Assert.Equal(new byte[] { 0xC6, 0x05 }, debug.AsSpan(debugImage.Offset(shotCave + 10), 2).ToArray());   // flags a set piece (after saving the direction asked)
        Assert.Equal(0x58CC16, debugImage.ReadInt32(shotCave + 21));         // set piece shots keep their power
        Assert.Equal(debugCave + 44, debugImage.ReadInt32(shotCave + 28));   // and those through the free kick cave
        // aim: the shot's kick call goes through code that brings the direction and height closer to the ones asked
        int kickCall = debugImage.Rel32Target(0x5BBC9A);
        Assert.NotEqual(0x478020, kickCall);
        Assert.Equal(0xE9, debug[debugImage.Offset(0x478020)]);              // still reaches the kick function's hook
        Assert.Throws<ArgumentOutOfRangeException>(() => ExePatcher.Build(original, options with { ShotPower = 40 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExePatcher.Build(original, options with { ShotAccuracy = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExePatcher.Build(original, options with { ShotAccuracy = 50, LobPower = 30 }));

        // each one alone: power without the aim hook, accuracy with the entry (it keeps the direction asked)
        var power = new PeImage(ExePatcher.Build(original, options with { ShotPower = 80 }));
        Assert.Equal(0x478020, power.Rel32Target(0x5BBC9A));
        var accuracy = new PeImage(ExePatcher.Build(original, options with { ShotAccuracy = 50 }));
        Assert.NotEqual(0x478020, accuracy.Rel32Target(0x5BBC9A));
        Assert.Equal(0x58CC16, accuracy.ReadInt32(accuracy.Rel32Target(0x5B9130) + 21));
        // a set piece (flagged by the entry) skips the accuracy: its kick call goes straight to the kick function
        int accuracyCave = accuracy.Rel32Target(0x5BBC9A), flag = accuracy.ReadInt32(accuracy.Rel32Target(0x5B9130) + 12);
        Assert.Equal(flag, accuracy.ReadInt32(accuracyCave + 22));   // after the human team check: cmp byte [set piece],0

        // a lob (L1 held, 0x400 in player+0xB0) keeps its height (95 %) and gets the lob power (70 %) on its speed
        var lob = ExePatcher.Build(original, options with { ShotAccuracy = 0, LobPower = 70 });
        var lobImage = new PeImage(lob);
        int lobCave = lobImage.Offset(lobImage.Rel32Target(0x5BBC9A));
        Assert.True(lob.AsSpan(lobCave, 200).IndexOf(new byte[] { 0xF7, 0x80, 0xB0, 0, 0, 0, 0x00, 0x04, 0, 0 }) > 0);
        Assert.True(lob.AsSpan(lobCave, 200).IndexOf(new byte[] { 0x8B, 0x44, 0x24, 0x10, 0x6B, 0xC0, 70 }) > 0);
        Assert.True(lob.AsSpan(lobCave, 200).IndexOf(new byte[] { 0x8B, 0x44, 0x24, 0x14, 0x6B, 0xC0, CodeCaves.LobHeight }) > 0);

        // alone, it makes .rdata writable and is detected as patched
        var alone = ExePatcher.Build(original, new PatchOptions(398, false, Display: false, FreeKick: true));
        Assert.Equal(0x80000000u, BitConverter.ToUInt32(alone, RdataFlagsOffset(alone)) & 0x80000000u);
        Assert.True(ExePatcher.Analyze(ExePatcher.Build(original, new PatchOptions(398, false, Display: false, FreeKick: true))).AlreadyPatched);

        static IEnumerable<int> Range(PeImage image, int va, int length) => Enumerable.Range(image.Offset(va), length);

        // "test eax,eax ; jne ; mov eax,percent" in the weight part
        static int PercentInCave(byte[] exe, PeImage image, int cave)
        {
            int at = exe.AsSpan(image.Offset(cave), FreeKickCaveSize).IndexOf(new byte[] { 0x85, 0xC0, 0x75, 0x05, 0xB8 });
            return BitConverter.ToInt32(exe, image.Offset(cave) + at + 5);
        }
    }

    /// <summary>Free kick cave without the test key: shot (56), kick (26), weight (51), 1 % of the weight (4).</summary>
    private const int FreeKickCaveSize = 56 + 26 + 51 + 4;

    [Fact]
    public void Spread_moves_the_targets_across_the_pitch_only()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        Assert.True(ExePatcher.Analyze(original).Spread);
        var options = new PatchOptions(398, FreeDirection: true, CloseDribble: true, SkipIntro: true);
        var without = ExePatcher.Build(original, options);
        var with = ExePatcher.Build(original, options with { Spread = 20 });

        // measured in game: the target setter (player, x, z) at 0x4C9DC0, players from 0x3BDC980 (0x240 each)
        var image = new PeImage(with);
        Assert.Equal(0xE9, with[image.Offset(0x4C9DC0)]);
        int cave = image.Rel32Target(0x4C9DC0);
        var code = with.AsSpan(image.Offset(cave), 200);
        Assert.True(code.IndexOf((byte[])[0xBF, .. BitConverter.GetBytes(0x3BDC980 + 0x240)]) >= 0);   // mov edi,players+1
        Assert.True(code.IndexOf((byte[])[0xB8, .. BitConverter.GetBytes(20)]) >= 0);                  // mov eax,20 %
        Assert.True(code.IndexOf(new byte[] { 0xD9, 0x5C, 0x24, 0x30 }) >= 0);                              // fstp [esp+0x30] ; z
        Assert.True(code.IndexOf(new byte[] { 0xD9, 0x5C, 0x24, 0x2C }) < 0);                               // x is left alone

        // only the setter's first 5 bytes and the cave change
        var changed = Enumerable.Range(0, with.Length).Where(i => with[i] != without[i]).ToList();
        Assert.All(changed, i => Assert.True(i - image.Offset(0x4C9DC0) is >= 0 and < 5 || i - image.Offset(cave) is >= 0 and < 200));
        Assert.True(ExePatcher.Analyze(ExePatcher.Build(original, new PatchOptions(398, false, Display: false, Spread: 20))).AnyFix);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExePatcher.Build(original, options with { Spread = 50 }));
    }

    [Fact]
    public void Speed_gap_rebuilds_the_maximum_running_speed_around_a_player_at_75()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        Assert.True(ExePatcher.Analyze(original).SpeedGap);
        var options = new PatchOptions(398, FreeDirection: true, CloseDribble: true, SkipIntro: true);
        var without = ExePatcher.Build(original, options);
        var with = ExePatcher.Build(original, options with { SpeedGap = 300 });

        // measured in game: the maximum speed formula at 0x447BF52 (protection section), stored at 0x447BF67
        var image = new PeImage(with);
        Assert.Equal(0xE9, with[image.Offset(0x447BF52)]);
        int cave = image.Rel32Target(0x447BF52);
        Assert.Equal(12 * 300, image.ReadInt32(cave + 2));                          // imul eax,eax,12*300
        Assert.Equal(0x1D76 + 12 * CodeCaves.SpeedPivot * (100 - 300) / 100, image.ReadInt32(cave + 14));   // add eax,offset
        Assert.Equal(0x447BF67, image.Rel32Target(cave + 29));                      // back to mov [esi+0x46],edx

        // a player at 75 keeps his speed: 7542 + 12 * 58 either way
        int stat = CodeCaves.SpeedPivot;
        Assert.Equal(0x1D76 + 12 * stat, 12 * 300 * stat / 100 + 0x1D76 + 12 * stat * (100 - 300) / 100);

        // only the formula's first 7 bytes and the cave change
        var changed = Enumerable.Range(0, with.Length).Where(i => with[i] != without[i]).ToList();
        Assert.All(changed, i => Assert.True(i - image.Offset(0x447BF52) is >= 0 and < 7 || i - image.Offset(cave) is >= 0 and < 40));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExePatcher.Build(original, options with { SpeedGap = 500 }));
    }

    [Fact]
    public void Pass_lead_scales_both_reads_of_the_lead_that_place_the_pass_point()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        Assert.True(ExePatcher.Analyze(original).PassLead);
        var options = new PatchOptions(398, FreeDirection: true, CloseDribble: true, SkipIntro: true);
        var without = ExePatcher.Build(original, options);
        var with = ExePatcher.Build(original, options with { PassLead = 60 });
        var image = new PeImage(with);

        // measured in game: mov eax,[edi+0xA8] at 0x4D0AF8 and mov ecx,[edi+0xA8] at 0x4D0B05
        Assert.Equal(new byte[] { 0x8B, 0x87, 0xA8, 0, 0, 0 }, without[image.Offset(0x4D0AF8)..(image.Offset(0x4D0AF8) + 6)]);
        Assert.Equal(new byte[] { 0x8B, 0x8F, 0xA8, 0, 0, 0 }, without[image.Offset(0x4D0B05)..(image.Offset(0x4D0B05) + 6)]);
        Assert.Equal(0xE9, with[image.Offset(0x4D0AF8)]);
        Assert.Equal(0xE9, with[image.Offset(0x4D0B05)]);
        int first = image.Rel32Target(0x4D0AF8), second = image.Rel32Target(0x4D0B05);
        Assert.Equal(new byte[] { 0x8B, 0x87, 0xA8, 0, 0, 0, 0x6B, 0xC0, 60 }, with[image.Offset(first)..(image.Offset(first) + 9)]);
        Assert.Equal(0x4D0AFE, image.Rel32Target(first + 15));                      // back after the first read
        Assert.Equal(new byte[] { 0x50, 0x52, 0x8B, 0x87, 0xA8, 0, 0, 0 }, with[image.Offset(second)..(image.Offset(second) + 8)]);
        Assert.Equal(0x4D0B0B, image.Rel32Target(second + 21));                     // back after the second read, ecx = scaled lead
        Assert.Throws<ArgumentOutOfRangeException>(() => ExePatcher.Build(original, options with { PassLead = 20 }));

        // with F9: the percent comes from the test keys' state (+30)
        var debug = ExePatcher.Build(original, options with { FreeKick = true, PassLead = 60, PassLeadDebug = true });
        var debugImage = new PeImage(debug);
        int debugFirst = debugImage.Offset(debugImage.Rel32Target(0x4D0AF8));
        Assert.True(debug.AsSpan(debugFirst, 40).IndexOf(new byte[] { 0x0F, 0xB7, 0x0D }) > 0);
    }

    [Fact]
    public void Carrier_turn_raises_the_two_turn_steps_with_the_ball()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        Assert.True(ExePatcher.Analyze(original).CarrierTurn);
        var options = new PatchOptions(398, FreeDirection: true, CloseDribble: true, SkipIntro: true);
        var without = ExePatcher.Build(original, options);
        var with = ExePatcher.Build(original, options with { CarrierTurn = PatchOptions.DefaultCarrierTurn });
        var image = new PeImage(with);

        // measured in game (write breakpoint on the carrier's +0x28): push 100h at 0x4C2088, add ecx,100h at 0x4C25C9
        int[] sites = [0x4C2089, 0x4C25CB];
        Assert.Equal(0xFA5, CodeCaves.CarrierTurnStep(22));   // 22 degrees: about 16 times the game's 0x100 (1.4 degrees)
        Assert.All(sites, va => Assert.Equal(0x100, BitConverter.ToInt32(without, image.Offset(va))));
        Assert.All(sites, va => Assert.Equal(0xFA5, image.ReadInt32(va)));
        var changed = Enumerable.Range(0, with.Length).Where(i => with[i] != without[i]).ToList();
        Assert.All(changed, i => Assert.True(sites.Any(va => i - image.Offset(va) is >= 0 and < 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExePatcher.Build(original, options with { CarrierTurn = 46 }));
    }

    [Fact]
    public void Stick_dead_zone_lowers_the_four_axis_limits_and_eases_the_set_piece_aims()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        Assert.True(ExePatcher.Analyze(original).StickDeadZone);
        var options = new PatchOptions(398, FreeDirection: true, CloseDribble: true, SkipIntro: true);
        var without = ExePatcher.Build(original, options);
        var with = ExePatcher.Build(original, options with { StickDeadZone = PatchOptions.DefaultStickDeadZone });
        var image = new PeImage(with);

        // measured in game: the imm8 of the 4 "cmp eax,25h" of the pad reader; 6 % of 127 -> 8
        int[] sites = [0x8B7D83, 0x8B7DAA, 0x8B7DD1, 0x8B7DFC];
        Assert.Equal(8, CodeCaves.StickDeadZoneLimit(PatchOptions.DefaultStickDeadZone));
        Assert.Equal(0x25, CodeCaves.StickDeadZoneLimit(PatchOptions.GameStickDeadZone));
        Assert.All(sites, va => Assert.Equal(0x25, without[image.Offset(va)]));
        Assert.All(sites, va => Assert.Equal(8, with[image.Offset(va)]));
        // measured in game: the corner (0x58EFE1) and free kick (0x58AEF6) aims, now hooked
        int[] aims = [0x58AEF6, 0x58EFE1];
        var caves = new List<int>();
        foreach (int va in aims)
        {
            Assert.Equal(0x84, without[image.Offset(va)]);                       // test al,al
            Assert.Equal(0xE9, with[image.Offset(va)]);                          // jmp cave
            int cave = va + 5 + image.ReadInt32(va + 1);
            caves.Add(cave);
            var expected = CodeCaves.SetPieceAim(cave, va);
            Assert.Equal(expected.EntryBytes, with.AsSpan(image.Offset(va), CodeCaves.SetPieceAimLength).ToArray());
            Assert.Equal(expected.CaveBytes, with.AsSpan(image.Offset(cave), expected.CaveBytes.Length).ToArray());
            Assert.Equal(va + CodeCaves.SetPieceAimLength, cave + expected.CaveBytes.Length + image.ReadInt32(cave + expected.CaveBytes.Length - 4));
        }
        var changed = Enumerable.Range(0, with.Length).Where(i => with[i] != without[i]).ToList();
        int caveSize = CodeCaves.SetPieceAim(0, 0).CaveBytes.Length;
        Assert.DoesNotContain(changed, i => !sites.Select(image.Offset).Contains(i)
            && !aims.Any(va => i >= image.Offset(va) && i < image.Offset(va) + CodeCaves.SetPieceAimLength)
            && !caves.Any(c => i >= image.Offset(c) && i < image.Offset(c) + caveSize));
        Assert.All(sites, va => Assert.Contains(image.Offset(va), changed));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExePatcher.Build(original, options with { StickDeadZone = 1 }));
    }

    [Fact]
    public void Lofted_aim_squares_the_gauge_and_narrows_the_receiver_angle()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        Assert.True(ExePatcher.Analyze(original).LoftedAim);
        var options = new PatchOptions(398, FreeDirection: true, CloseDribble: true, SkipIntro: true);
        var without = ExePatcher.Build(original, options);
        var with = ExePatcher.Build(original, options with { LoftedAim = true });
        var image = new PeImage(with);

        // measured in game: the gauge conversion at 0x5690F4 (12 bytes) and the angle byte of cmp word [esp+0x1C],0x20 at 0x569394
        Assert.Equal(new byte[] { 0x0F, 0xB6, 0xC0, 0xC1, 0xE8, 0x03, 0x69, 0xC0, 0x80, 0x03, 0x00, 0x00 }, without[image.Offset(0x5690F4)..(image.Offset(0x5690F4) + 12)]);
        Assert.Equal(CodeCaves.LoftedGaugeSquare, with[image.Offset(0x5690F4)..(image.Offset(0x5690F4) + 12)]);
        Assert.Equal(0x20, without[image.Offset(0x569394)]);
        Assert.Equal(CodeCaves.LoftedAngle, with[image.Offset(0x569394)]);
        var changed = Enumerable.Range(0, with.Length).Where(i => with[i] != without[i]).ToList();
        Assert.All(changed, i => Assert.True(i - image.Offset(0x5690F4) is >= 0 and < 12 || i == image.Offset(0x569394)));

        // same distance as the game at a full gauge (0x80): 0x80 * 0x80 * 7 / 8 = 0x3800
        Assert.Equal(0x3800, 0x80 * 0x80 * 7 / 8);
    }

    [Fact]
    public void Tackle_widens_the_pressing_challenge_reach_and_the_attempt_distance()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        Assert.True(ExePatcher.Analyze(original).Tackle);
        var options = new PatchOptions(398, FreeDirection: true, CloseDribble: true, SkipIntro: true);
        var without = ExePatcher.Build(original, options);

        // measured in game: cmp edi,0xA0 ; jge ; push 0x50 at 0x572AE8, before the take-ball attempt 0x461DC0
        var image = new PeImage(without);
        Assert.Equal(new byte[] { 0x81, 0xFF, 0xA0, 0x00, 0x00, 0x00, 0x7D, 0x5B, 0x6A, 0x50 }, without[image.Offset(0x572AE8)..(image.Offset(0x572AE8) + 10)]);

        // 180 %: reach 288, parameter 144 capped at 127 (a signed byte)
        var with = ExePatcher.Build(original, options with { Tackle = 180 });
        Assert.Equal(0x120, new PeImage(with).ReadInt32(0x572AEA));
        Assert.Equal(0x7F, with[image.Offset(0x572AF1)]);
        var changed = Enumerable.Range(0, with.Length).Where(i => with[i] != without[i]).ToList();
        Assert.All(changed, i => Assert.True(i - image.Offset(0x572AEA) is >= 0 and < 4 || i == image.Offset(0x572AF1)));
        Assert.Equal((0xA0 * 140 / 100, (byte)112), CodeCaves.TackleValues(140));

        // with F8: a jump to a cave that comes back to push edi, or goes to the attempt's skip (0x572B4B)
        var debug = ExePatcher.Build(original, options with { Tackle = 180, TackleDebug = true });
        image = new PeImage(debug);
        Assert.Equal(0xE9, debug[image.Offset(0x572AE8)]);
        int cave = image.Rel32Target(0x572AE8);
        Assert.Contains(Enumerable.Range(cave, 60), a => debug[image.Offset(a)] == 0xE9 && image.Rel32Target(a) == 0x572B4B);
        Assert.Contains(Enumerable.Range(cave, 70), a => debug[image.Offset(a)] == 0xE9 && image.Rel32Target(a) == 0x572AF2);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExePatcher.Build(original, options with { Tackle = 200 }));
    }

    [Fact]
    public void Sprint_turns_raise_the_turn_limit_turn_the_player_at_both_touches_and_play_the_turning_animation_sooner()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        Assert.True(ExePatcher.Analyze(original).SprintTurn);
        var options = new PatchOptions(398, FreeDirection: true, CloseDribble: true, SkipIntro: true);
        var without = ExePatcher.Build(original, options);
        var with = ExePatcher.Build(original, options with { SprintTurn = 90 });
        var image = new PeImage(with);

        // measured in game: push 0x1000 at 0x4E2AE6 (ball turn limit of a sprint stride), touch at 0x5AFB32
        Assert.Equal(new byte[] { 0x68, 0x00, 0x10, 0x00, 0x00 }, without[image.Offset(0x4E2AE6)..(image.Offset(0x4E2AE6) + 5)]);
        Assert.Equal(0xE9, with[image.Offset(0x4E2AE6)]);
        int limit = image.Rel32Target(0x4E2AE6);
        Assert.Equal(0x3BCF5C0, image.ReadInt32(limit + 23));                       // mov eax,[eax+dribble accuracy]
        Assert.Contains(BitConverter.GetBytes(CodeCaves.Angle(90)), Chunks(with, image.Offset(limit), 83));
        Assert.Equal(0x4E2AEB, image.Rel32Target(limit + 78));                      // back after the push

        // both sprint touches (one per foot) turn the player with the ball
        Assert.Equal(0xE9, with[image.Offset(0x5AFB32)]);
        Assert.Equal(new byte[] { 0x90, 0x90 }, with[(image.Offset(0x5AFB32) + 5)..(image.Offset(0x5AFB32) + 7)]);
        int touch = image.Rel32Target(0x5AFB32);
        Assert.Equal(0xBCCE94, image.ReadInt32(touch + 4));                         // ball pointer
        Assert.Equal(new byte[] { 0x50, 0x33, 0xC0, 0x66, 0x8B, 0x47, 0x30 }, with[(image.Offset(touch) + 53)..(image.Offset(touch) + 60)]);
        Assert.Equal(0x5AFB39, image.Rel32Target(touch + 60));                      // back after the 7 replaced bytes

        Assert.Equal(0xE9, with[image.Offset(0x5AFE5C)]);
        Assert.Equal(new byte[] { 0x90, 0x90, 0x90, 0x90 }, with[(image.Offset(0x5AFE5C) + 5)..(image.Offset(0x5AFE5C) + 9)]);
        int other = image.Rel32Target(0x5AFE5C);
        Assert.Equal(0xBCCE94, image.ReadInt32(other + 4));
        Assert.Equal(new byte[] { 0x33, 0xC9, 0x66, 0x8B, 0x4F, 0x30, 0x83, 0xC4, 0x0C }, with[(image.Offset(other) + 53)..(image.Offset(other) + 62)]);
        Assert.Equal(0x5AFE65, image.Rel32Target(other + 62));                      // back after the 9 replaced bytes

        // the turning animation from 45 degrees (cmp ax,0x50 -> 0x20 at 0x4E287C)
        Assert.Equal(0x50, without[image.Offset(0x4E287E)]);
        Assert.Equal(0x20, with[image.Offset(0x4E287E)]);

        // only the entries, their caves and the animation byte change
        var changed = Enumerable.Range(0, with.Length).Where(i => with[i] != without[i]).ToList();
        Assert.All(changed, i => Assert.True(i - image.Offset(0x4E2AE6) is >= 0 and < 5 || i - image.Offset(0x5AFB32) is >= 0 and < 7
            || i - image.Offset(0x5AFE5C) is >= 0 and < 9 || i == image.Offset(0x4E287E)
            || i - image.Offset(limit) is >= 0 and < 83 || i - image.Offset(touch) is >= 0 and < 65 || i - image.Offset(other) is >= 0 and < 67));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExePatcher.Build(original, options with { SprintTurn = 30 }));

        // with F7, the maximum is read from the test keys' state (+26), in degrees
        var debug = ExePatcher.Build(original, options with { SprintTurn = 90, SprintTurnDebug = true });
        image = new PeImage(debug);
        Assert.Equal(0xE9, debug[image.Offset(0x4E2AE6)]);

        static IEnumerable<byte[]> Chunks(byte[] bytes, int from, int length) =>
            Enumerable.Range(from, length - 3).Select(i => bytes[i..(i + 4)]);
    }

    [Fact]
    public void Skip_intro_only_turns_the_three_skip_jumps_into_nops()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        Assert.True(ExePatcher.Analyze(original).SkipIntro);
        var options = new PatchOptions(398, FreeDirection: true, CloseDribble: true);
        var without = ExePatcher.Build(original, options);
        var with = ExePatcher.Build(original, options with { SkipIntro = true });

        // movie: je at 0x88E361 ; logos: je at 0x9AFAD5 and 0x9AFAE4 (measured in game)
        var image = new PeImage(original);
        int[] jumps = [0x88E361, 0x9AFAD5, 0x9AFAE4];
        var expected = jumps.SelectMany(j => new[] { image.Offset(j), image.Offset(j) + 1 }).Order().ToList();
        var changed = Enumerable.Range(0, with.Length).Where(i => with[i] != without[i]).ToList();
        Assert.Equal(expected, changed);
        Assert.All(changed, i => Assert.Equal(0x90, with[i]));
        Assert.All(jumps, j => Assert.Equal(0x74, original[image.Offset(j)]));
    }

    [Fact]
    public void Skip_intro_alone_is_detected_as_patched()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        var built = ExePatcher.Build(original, new PatchOptions(398, FreeDirection: false, Display: false, SkipIntro: true));
        Assert.True(ExePatcher.Analyze(built).AlreadyPatched);
        Assert.False(ExePatcher.IsSupported(built));
    }

    [Fact]
    public void Build_refuses_an_exe_where_a_location_is_missing()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        var altered = (byte[])original.Clone();
        altered[new PeImage(original).Offset(0x40D740)] ^= 0xFF;   // clip function entry
        var report = ExePatcher.Analyze(altered);
        Assert.False(report.Display);
        Assert.NotEmpty(report.Problems);
        Assert.Throws<PatchException>(() => ExePatcher.Build(altered, new PatchOptions(398, true)));
    }

    [Fact]
    public void Analyze_recognises_the_validated_build_and_finds_every_location()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        var report = ExePatcher.Analyze(original);
        Assert.Empty(report.Problems);
        Assert.True(report.Validated);
        Assert.False(report.AlreadyPatched);
        Assert.True(report.Display);
        Assert.True(report.FreeDirection);
    }

    [Theory]
    [InlineData("PES6_v6_16x9.exe")]
    [InlineData("PES6_v6_32x9.exe")]
    public void Analyze_detects_an_exe_patched_by_earlier_versions(string patched)
    {
        var exe = Load(patched);
        if (exe == null) return;

        var report = ExePatcher.Analyze(exe);
        Assert.True(report.AlreadyPatched);
        Assert.False(ExePatcher.IsSupported(exe));
    }

    [Fact]
    public void Report_names_the_file_its_hash_and_what_can_be_applied()
    {
        var path = Reference == null ? null : Path.Combine(Reference, "PES6.orig.exe");
        if (path == null || !File.Exists(path)) return;

        var text = Diagnostics.Report(path);
        Assert.Contains("PES6.orig.exe", text);
        Assert.Contains(ExePatcher.ValidatedSha256.First(), text);
    }

    [Fact]
    public void A_build_with_another_hash_but_the_same_code_is_patched_the_same_way()
    {
        var original = Load("PES6.orig.exe");
        var expected = Load("PES6_v6_16x9.exe");
        if (original == null || expected == null) return;

        // change the PE timestamp: another hash, same code
        var other = (byte[])original.Clone();
        int timestamp = BitConverter.ToInt32(other, 0x3C) + 8;
        other[timestamp] ^= 0x5A;

        var report = ExePatcher.Analyze(other);
        Assert.False(report.Validated);
        Assert.True(report.Display && report.FreeDirection);

        var options = new PatchOptions(398, FreeDirection: true, CloseDribble: true);
        var built = ExePatcher.Build(other, options);
        var reference = ExePatcher.Build(original, options);
        reference[timestamp] ^= 0x5A;
        Assert.Equal(reference, built);
        expected[timestamp] ^= 0x5A;
        AssertSameExceptKickRelay(expected, built, CodeCaves.KickRelaySize(direction: true, closeDribble: true));
    }

    [Fact]
    public void Free_direction_rounds_the_snapped_dribble_touch_back_to_the_stick_angle()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        // only the free direction: the cave starts with the snapped touch test, then the pad loop
        var built = ExePatcher.Build(original, new PatchOptions(398, FreeDirection: true, Display: false));
        var image = new PeImage(built);
        int relay = image.Offset(KickRelay);
        int cave = image.Offset(KickRelay + 5 + BitConverter.ToInt32(built, relay + 1));
        Assert.Equal(0x3D, built[cave + 3]);
        Assert.Equal(0x5ADEC6, BitConverter.ToInt32(built, cave + 4));
        Assert.Equal(0x1016682, BitConverter.ToInt32(built, cave + 18));   // pad 0 stick magnitude
        Assert.Equal(0x1016680, BitConverter.ToInt32(built, cave + 29));   // pad 0 stick angle

        // close dribbling alone is unchanged since 1.4: no direction part
        var close = ExePatcher.Build(original, new PatchOptions(398, FreeDirection: false, Display: false, CloseDribble: true));
        var closeImage = new PeImage(close);
        int closeCave = closeImage.Offset(KickRelay + 5 + BitConverter.ToInt32(close, closeImage.Offset(KickRelay) + 1));
        Assert.Equal(0x5AF6D3, BitConverter.ToInt32(close, closeCave + CloseDribbleSwitchSize + 4));
    }

    /// <summary>
    /// F1 switch at the start of the close dribbling part: set-up call on the first touch, then (when the
    /// Kitserver message is not installed) push ; call [GetAsyncKeyState] ; test ; je ; xor ; then cmp ; jne.
    /// </summary>
    private const int CloseDribbleSwitchSize = 51;

    private const int SwitchState = 0xBC3FF0;   // last 16 bytes of .rdata

    [Fact]
    public void Close_dribble_can_be_switched_with_F1_through_a_flag_in_writable_rdata()
    {
        var original = Load("PES6.orig.exe");
        if (original == null) return;

        var built = ExePatcher.Build(original, new PatchOptions(398, FreeDirection: false, Display: false, CloseDribble: true));
        var image = new PeImage(built);
        int relayCave = KickRelay + 5 + BitConverter.ToInt32(built, image.Offset(KickRelay) + 1);
        int cave = image.Offset(relayCave);

        Assert.Equal(SwitchState + 1, BitConverter.ToInt32(built, cave + 2));          // cmp byte [set up],0
        int setUp = relayCave + 14 + BitConverter.ToInt32(built, cave + 10);          // call set-up
        Assert.Equal(SwitchState + 2, BitConverter.ToInt32(built, cave + 16));         // cmp byte [message hook],0
        Assert.Equal([0x6A, 0x70, 0xFF, 0x15], built.AsSpan(cave + 23, 4).ToArray());  // push VK_F1 ; call [...]
        Assert.Equal(0xB7D2E0, BitConverter.ToInt32(built, cave + 27));               // user32!GetAsyncKeyState
        Assert.Equal(SwitchState, BitConverter.ToInt32(built, cave + 37));             // xor byte [off],1
        Assert.Equal(SwitchState, BitConverter.ToInt32(built, cave + 44));             // cmp byte [off],0
        Assert.Equal(0x80000000u, BitConverter.ToUInt32(built, RdataFlagsOffset(built)) & 0x80000000u);

        // the set-up cave asks kload.dll for HookFunction and KDrawText, and holds both messages
        var text = System.Text.Encoding.ASCII.GetString(built, image.Offset(setUp), 400);
        Assert.Contains("kload.dll\0KDrawText\0HookFunction\0", text);
        Assert.Contains(ExePatcher.Ascii(Loc.T("Overlay.CloseDribbleOn")) + "\0", text);
        Assert.Contains(ExePatcher.Ascii(Loc.T("Overlay.CloseDribbleOff")) + "\0", text);
        Assert.Equal(0u, BitConverter.ToUInt32(original, RdataFlagsOffset(original)) & 0x80000000u);

        // without close dribbling, .rdata stays read-only
        var without = ExePatcher.Build(original, new PatchOptions(398, FreeDirection: true));
        Assert.Equal(0u, BitConverter.ToUInt32(without, RdataFlagsOffset(without)) & 0x80000000u);
    }

    [Theory]
    [InlineData(1920, 1080, 398)]
    [InlineData(2560, 1440, 398)]
    [InlineData(5120, 1440, 796)]
    [InlineData(3440, 1440, 535)]
    public void Half_width_follows_the_aspect_ratio(int width, int height, int half) =>
        Assert.Equal(half, new InstallSettings(width, height, true, true, 1.0).Half);

    [Fact]
    public void Kload_for_32x9_matches_the_validated_profile()
    {
        var before = Load(@"profiles\16x9\kload.cfg");
        var expected = Load(@"profiles\32x9\kload.cfg");
        if (before == null || expected == null) return;

        var text = KloadConfig.Apply(System.Text.Encoding.ASCII.GetString(before), 5120, 1440, borderlessWindow: true);
        Assert.Equal(System.Text.Encoding.ASCII.GetString(expected), text);
    }

    [Fact]
    public void Kload_without_borderless_window_comments_the_window_lines_out()
    {
        var text = KloadConfig.Apply("dx.window.x = 0\r\ndx.window.width = 5120\r\ninternal.resolution.width = 0\r\n", 2560, 1440, borderlessWindow: false);
        Assert.Contains("#dx.window.x = 0", text);
        Assert.Contains("#dx.window.width = 5120", text);
        Assert.Contains("internal.resolution.width = 2560", text);
    }

    [Theory]
    [InlineData(5760, 3240, 1920, 1080, true)]    // supersampling, same aspect ratio
    [InlineData(1920, 1080, 1920, 1080, true)]
    [InlineData(1920, 1080, 2560, 1440, false)]   // smaller than the display
    [InlineData(2560, 1440, 5120, 1440, false)]   // other aspect ratio
    [InlineData(0, 0, 1920, 1080, false)]
    public void Kload_keeps_a_supersampled_internal_resolution(int iw, int ih, int width, int height, bool kept)
    {
        var text = KloadConfig.Apply($"internal.resolution.width = {iw}\r\ninternal.resolution.height = {ih}\r\n", width, height, borderlessWindow: false);
        var (w, h) = kept ? (iw, ih) : (width, height);
        Assert.Contains($"internal.resolution.width = {w}\r\ninternal.resolution.height = {h}", text);
    }

    private const string KloadWithDlls =
        "debug = 1\r\n\r\nDLL.num = 3\r\nDLL.0 = \"kitserver\\zlib1.dll\"\r\nDLL.1 = \"kitserver\\lodmixer.dll\"\r\nDLL.2 = \"kitserver\\speeder.dll\"\r\n\r\ndx.fullscreen.width = 1920\r\n";

    [Fact]
    public void Physics_mod_is_added_at_the_end_of_the_dll_list_and_removed_cleanly()
    {
        var added = KloadConfig.SetDll(KloadWithDlls, PhysicsMod.KloadPath, loaded: true);
        Assert.Contains("DLL.num = 4\r\n", added);
        Assert.Contains("DLL.2 = \"kitserver\\speeder.dll\"\r\nDLL.3 = \"kitserver\\pes6_game_physics_mod.dll\"\r\n\r\ndx.fullscreen.width = 1920", added);
        Assert.Equal(added, KloadConfig.SetDll(added, PhysicsMod.KloadPath, loaded: true));   // already there

        Assert.Equal(KloadWithDlls, KloadConfig.SetDll(added, PhysicsMod.KloadPath, loaded: false));
        Assert.Equal(KloadWithDlls, KloadConfig.SetDll(KloadWithDlls, PhysicsMod.KloadPath, loaded: false));
    }

    [Fact]
    public void Removing_a_dll_in_the_middle_renumbers_the_list()
    {
        var text = KloadWithDlls.Replace("DLL.num = 3", "DLL.num = 4").Replace("DLL.2 = ", "DLL.3 = ")
            .Replace("DLL.1 = \"kitserver\\lodmixer.dll\"\r\n", "DLL.1 = \"kitserver\\lodmixer.dll\"\r\nDLL.2 = \"kitserver\\PES6_GAME_PHYSICS_MOD.DLL\"\r\n");
        Assert.Equal(KloadWithDlls, KloadConfig.SetDll(text, PhysicsMod.KloadPath, loaded: false));
        Assert.Equal(["kitserver\\zlib1.dll", "kitserver\\lodmixer.dll", "kitserver\\speeder.dll"], KloadConfig.Dlls(KloadWithDlls));
    }

    [Fact]
    public void Lodmixer_of_another_format_is_not_recognised()
    {
        Assert.False(LodmixerData.IsRecognized(new byte[41]));
        Assert.True(LodmixerData.IsRecognized(new byte[45]));
    }

    [Fact]
    public void Lodmixer_and_settings_for_32x9_match_the_validated_profile()
    {
        var lod16 = Load(@"profiles\16x9\lodmixer.dat");
        var lod32 = Load(@"profiles\32x9\lodmixer.dat");
        var set16 = Load(@"profiles\16x9\settings.dat");
        var set32 = Load(@"profiles\32x9\settings.dat");
        if (lod16 == null || lod32 == null || set16 == null || set32 == null) return;

        Assert.Equal(lod32, LodmixerData.SetAspect(lod16, 32f / 9));
        Assert.True(GameSettingsData.HasValidChecksum(set16));
        Assert.Equal(set32, GameSettingsData.SetResolution(set16, 5120, 1440));
    }

    [Fact]
    public void Settings_checksum_is_the_sum_of_the_bytes_from_offset_4()
    {
        var data = new byte[] { 3, 0, 0, 0, 0x80, 0x07, 0x38, 0x04, 1, 2, 3 };
        var updated = GameSettingsData.SetResolution(data, 5120, 1440);
        Assert.Equal((5120, 1440), GameSettingsData.GetResolution(updated));
        Assert.True(GameSettingsData.HasValidChecksum(updated));
    }
}
