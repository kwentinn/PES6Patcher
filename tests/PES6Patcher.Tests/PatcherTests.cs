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
