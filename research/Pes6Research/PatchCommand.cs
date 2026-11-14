using System.Text.Json;

namespace Pes6Research;

/// <summary>
/// Applies or reverts the named memory patches of research/patches.json in the running game (was pes6patch.ps1): one command to
/// try an idea and one to put the game back. The original bytes are checked first, so nothing is written over another patch or
/// build: a patch whose bytes are neither the original nor the patched ones is refused unless -Force.
/// To add a patch, add an entry to patches.json with the bytes read BEFORE the change (original) and the new ones.
/// </summary>
static class PatchCommand
{
    public static readonly string[] Switches = ["List", "RevertAll", "Force"];
    public const string Usage = "patch [-List] [-Apply a,b] [-Revert a,b] [-RevertAll] [-Force] [-File patches.json]\n" +
                                "                                                 named memory patches, original bytes checked first";

    public static int Run(Options o, string processName)
    {
        var list = o.Flag("List");
        var apply = o.List("Apply");
        var revert = o.List("Revert");
        var revertAll = o.Flag("RevertAll");
        var force = o.Flag("Force");
        var patches = PatchSet.Load(o.String("File"));
        o.CheckAllUsed();

        var acting = apply.Count > 0 || revert.Count > 0 || revertAll;
        var running = GameProcess.IsRunning(processName);
        if (acting && !running) throw new ToolException($"{processName} is not running.");

        using var game = running ? GameProcess.Open(processName, write: acting) : null;
        if (list || !acting)
            foreach (var name in patches.Names)
                Console.WriteLine($"{name,-20} {(game == null ? "game not running" : patches.State(game, name)),-16} {patches.About(name)}");
        if (game == null) return 0;

        if (revertAll) revert = [.. patches.Names];
        var failed = 0;
        foreach (var n in apply) if (!patches.Set(game, n, on: true, force)) failed++;
        foreach (var n in revert) if (!patches.Set(game, n, on: false, force)) failed++;
        return failed == 0 ? 0 : 1;
    }
}

/// <summary>The named patches of patches.json, applied and reverted in a game opened for writing.</summary>
sealed class PatchSet
{
    sealed record Entry(string Address, string Original, string Patched);
    sealed record Patch(string About, Entry[] Bytes);

    readonly Dictionary<string, Patch> patches;

    PatchSet(Dictionary<string, Patch> patches) { this.patches = patches; }

    /// <summary>The file given, or research/patches.json found from the exe (research/Pes6Research/bin) or the current folder.</summary>
    public static PatchSet Load(string? file)
    {
        file ??= FindPatchesFile();
        if (!File.Exists(file)) throw new ToolException($"{file}: no such file");
        return new(JsonSerializer.Deserialize<Dictionary<string, Patch>>(File.ReadAllText(file),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? []);
    }

    public IEnumerable<string> Names => patches.Keys;

    public bool Contains(string name) => patches.ContainsKey(name);

    public string About(string name) => patches[name].About;

    /// <summary>"applied", "original", or "other (bytes)"; several states joined by " / " when the places differ.</summary>
    public string State(GameProcess game, string name) => string.Join(" / ", patches[name].Bytes.Select(e => State(game, e)).Distinct());

    /// <summary>Applies (on) or reverts the patch and prints the outcome; false when refused or not in place afterwards.</summary>
    public bool Set(GameProcess game, string name, bool on, bool force)
    {
        if (!patches.TryGetValue(name, out var p)) { Console.Error.WriteLine($"unknown patch '{name}' (see -List)"); return false; }
        var want = on ? "applied" : "original";
        var states = p.Bytes.Select(e => State(game, e)).ToList();
        if (!force && states.Any(s => s.StartsWith("other")))
        {
            Console.Error.WriteLine($"{name} : unexpected bytes ({string.Join(" ; ", states)}): another build or patch? use -Force to write anyway");
            return false;
        }
        foreach (var e in p.Bytes) game.Write(Address(e), CodeCommand.ParseHex(on ? e.Patched : e.Original));
        var after = p.Bytes.Select(e => State(game, e)).ToList();
        var ok = after.All(s => s == want);
        Console.WriteLine($"{name}: {(ok ? want : $"NOT {want} ({string.Join(" ; ", after)})")}");
        return ok;
    }

    /// <summary>The bytes now at every place of the patch, to put the game back exactly as it was (see <see cref="Restore"/>).</summary>
    public (string Name, (uint Address, byte[] Bytes)[] Places) Snapshot(GameProcess game, string name) =>
        (name, [.. patches[name].Bytes.Select(e => (Address(e), game.Read(Address(e), CodeCommand.ParseHex(e.Original).Length)))]);

    /// <summary>Writes the bytes of a snapshot back and prints the patch's state: the game is as before the run, whatever it was.</summary>
    public void Restore(GameProcess game, (string Name, (uint Address, byte[] Bytes)[] Places) snapshot)
    {
        foreach (var (address, bytes) in snapshot.Places) game.Write(address, bytes);
        Console.WriteLine($"{snapshot.Name}: put back as before ({State(game, snapshot.Name)})");
    }

    static string State(GameProcess game, Entry e)
    {
        var original = CodeCommand.ParseHex(e.Original);
        var now = new byte[original.Length];
        if (!game.TryRead(Address(e), now)) return "other (unreadable)";
        if (now.AsSpan().SequenceEqual(CodeCommand.ParseHex(e.Patched))) return "applied";
        if (now.AsSpan().SequenceEqual(original)) return "original";
        return $"other ({CodeCommand.FormatHex(now)})";
    }

    static uint Address(Entry e) => (uint)Options.ParseInt(e.Address, "address");

    static string FindPatchesFile()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                foreach (var f in new[] { Path.Combine(d.FullName, "patches.json"), Path.Combine(d.FullName, "research", "patches.json") })
                    if (File.Exists(f)) return f;
        throw new ToolException("patches.json not found: pass -File");
    }
}
