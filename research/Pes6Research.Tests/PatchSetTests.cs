using Pes6Research;

namespace Pes6Research.Tests;

public class PatchSetTests
{
    // two places, as defence-line
    const string Json = """
        {
          "line": {
            "about": "two places",
            "bytes": [
              { "address": "0x5E1E67", "original": "00 F6 FF FF", "patched": "00 00 00 00" },
              { "address": "0x5E1FC6", "original": "00 0A 00 00", "patched": "00 00 00 00" }
            ]
          },
          "speed": { "about": "one byte", "bytes": [ { "address": "0x5E2000", "original": "3C", "patched": "1E" } ] }
        }
        """;

    static (PatchSet Patches, FakeMemory Memory) Original()
    {
        var m = new FakeMemory(0x5E0000, 0x10000);
        m.Set(0x5E1E67, 0x00, 0xF6, 0xFF, 0xFF);
        m.Set(0x5E1FC6, 0x00, 0x0A, 0x00, 0x00);
        m.Set(0x5E2000, 0x3C);
        return (PatchSet.Parse(Json), m);
    }

    static bool Set(PatchSet p, FakeMemory m, string name, bool on, bool force = false) =>
        ConsoleOutput.Capture(() => p.Set(m, name, on, force)).Result;

    [Fact]
    public void The_state_tells_original_applied_and_other_bytes()
    {
        var (p, m) = Original();
        Assert.Equal("original", p.State(m, "line"));
        m.Set(0x5E1E67, 0, 0, 0, 0);
        Assert.Equal("applied / original", p.State(m, "line")); // one place of two
        m.Set(0x5E1FC6, 0, 0, 0, 0);
        Assert.Equal("applied", p.State(m, "line"));
        m.Set(0x5E2000, 0x55);
        Assert.Equal("other (55)", p.State(m, "speed"));
    }

    [Fact]
    public void Apply_then_revert_writes_every_place()
    {
        var (p, m) = Original();
        Assert.True(Set(p, m, "line", on: true));
        Assert.Equal(new byte[4], m.Get(0x5E1E67, 4));
        Assert.Equal(new byte[4], m.Get(0x5E1FC6, 4));
        Assert.True(Set(p, m, "line", on: false));
        Assert.Equal("original", p.State(m, "line"));
    }

    [Fact]
    public void Bytes_of_another_build_or_patch_are_not_overwritten_unless_forced()
    {
        var (p, m) = Original();
        m.Set(0x5E1E67, 0x00, 0xFB, 0xFF, 0xFF); // defence-line-half
        var (ok, output) = ConsoleOutput.Capture(() => p.Set(m, "line", on: true, force: false));
        Assert.False(ok);
        Assert.Contains("unexpected bytes", output);
        Assert.Equal(0, m.Writes);
        Assert.True(Set(p, m, "line", on: true, force: true));
        Assert.Equal("applied", p.State(m, "line"));
    }

    [Fact]
    public void An_unknown_patch_is_refused()
    {
        var (p, m) = Original();
        Assert.False(Set(p, m, "nope", on: true));
    }

    [Fact]
    public void An_unreadable_place_is_other_and_refused()
    {
        var p = PatchSet.Parse(Json);
        var m = new FakeMemory(0x100000, 0x100); // the patch addresses are not in it
        Assert.Equal("other (unreadable)", p.State(m, "line"));
        Assert.False(Set(p, m, "line", on: true));
    }

    [Fact]
    public void Restore_puts_back_the_bytes_found_even_when_the_patch_was_applied()
    {
        // the bug of the first live A/B: the patcher had applied the patch in the exe, the run ended with it reverted
        var (p, m) = Original();
        Set(p, m, "line", on: true);
        var before = p.Snapshot(m, "line");
        Set(p, m, "line", on: false);
        ConsoleOutput.Capture(() => { p.Restore(m, before); return 0; });
        Assert.Equal("applied", p.State(m, "line"));
    }

    [Fact]
    public void Restore_keeps_bytes_that_are_neither_original_nor_patched()
    {
        var (p, m) = Original();
        m.Set(0x5E2000, 0x00); // a stand-in whose zone is still zero
        var before = p.Snapshot(m, "speed");
        Set(p, m, "speed", on: true, force: true);
        ConsoleOutput.Capture(() => { p.Restore(m, before); return 0; });
        Assert.Equal(new byte[] { 0x00 }, m.Get(0x5E2000, 1));
    }

    [Theory]
    [InlineData("""{ "x": { "about": "", "bytes": [ { "address": "0x10", "original": "00 00", "patched": "01" } ] } }""")]
    [InlineData("""{ "x": { "about": "", "bytes": [ { "address": "zz", "original": "00", "patched": "01" } ] } }""")]
    [InlineData("""{ "x": { "about": "", "bytes": [] } }""")]
    [InlineData("""{ "x": { "about": "", "bytes": [ { "address": "0x10", "original": "", "patched": "" } ] } }""")]
    [InlineData("""{ "x": """)]
    public void A_broken_patches_file_is_refused_when_loaded(string json) => Assert.Throws<ToolException>(() => PatchSet.Parse(json));

    [Fact]
    public void The_repository_patches_file_loads()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "patches.json");
        var p = PatchSet.Load(Path.GetFullPath(file));
        Assert.Contains("defence-line", p.Names);
        Assert.Contains("speed-x2", p.Names);
    }
}
