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

        var diff = Enumerable.Range(0, built.Length).Where(i => built[i] != expected[i]).Take(10).ToList();
        Assert.True(diff.Count == 0, "Octets différents aux offsets : " + string.Join(", ", diff.Select(i => $"0x{i:X}")));
    }

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

        var built = ExePatcher.Build(other, new PatchOptions(398, FreeDirection: true));
        expected[timestamp] ^= 0x5A;
        Assert.Equal(expected, built);
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
