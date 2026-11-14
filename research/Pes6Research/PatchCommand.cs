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

    sealed record Entry(string Address, string Original, string Patched);
    sealed record Patch(string About, Entry[] Bytes);

    public static int Run(Options o, string processName)
    {
        var list = o.Flag("List");
        var apply = o.List("Apply");
        var revert = o.List("Revert");
        var revertAll = o.Flag("RevertAll");
        var force = o.Flag("Force");
        var file = o.String("File") ?? FindPatchesFile();
        o.CheckAllUsed();

        var patches = JsonSerializer.Deserialize<Dictionary<string, Patch>>(File.ReadAllText(file),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        var acting = apply.Count > 0 || revert.Count > 0 || revertAll;
        var running = GameProcess.IsRunning(processName);
        if (acting && !running) throw new ToolException($"{processName} is not running.");

        using var game = running ? GameProcess.Open(processName, write: acting) : null;
        if (list || !acting)
            foreach (var (name, p) in patches)
            {
                var state = game == null ? "game not running" : string.Join(" / ", p.Bytes.Select(e => State(game, e)).Distinct());
                Console.WriteLine($"{name,-20} {state,-16} {p.About}");
            }
        if (game == null) return 0;

        if (revertAll) revert = [.. patches.Keys];
        var failed = 0;
        foreach (var n in apply) if (!Set(game, patches, n, on: true, force)) failed++;
        foreach (var n in revert) if (!Set(game, patches, n, on: false, force)) failed++;
        return failed == 0 ? 0 : 1;
    }

    static bool Set(GameProcess game, Dictionary<string, Patch> patches, string name, bool on, bool force)
    {
        if (!patches.TryGetValue(name, out var p)) { Console.Error.WriteLine($"unknown patch '{name}' (see -List)"); return false; }
        var want = on ? "applied" : "original";
        var states = p.Bytes.Select(e => State(game, e)).ToList();
        if (!force && states.Any(s => s.StartsWith("other")))
        {
            Console.Error.WriteLine($"{name} : unexpected bytes ({string.Join(" ; ", states)}): another build or patch? use -Force to write anyway");
            return false;
        }
        foreach (var e in p.Bytes) game.Write(Address(e), Hex(on ? e.Patched : e.Original));
        var after = p.Bytes.Select(e => State(game, e)).ToList();
        var ok = after.All(s => s == want);
        Console.WriteLine($"{name}: {(ok ? want : $"NOT {want} ({string.Join(" ; ", after)})")}");
        return ok;
    }

    static string State(GameProcess game, Entry e)
    {
        var original = Hex(e.Original);
        var now = new byte[original.Length];
        if (!game.TryRead(Address(e), now)) return "other (unreadable)";
        if (now.AsSpan().SequenceEqual(Hex(e.Patched))) return "applied";
        if (now.AsSpan().SequenceEqual(original)) return "original";
        return $"other ({BitConverter.ToString(now).Replace('-', ' ')})";
    }

    static uint Address(Entry e) => (uint)Options.ParseInt(e.Address, "address");

    static byte[] Hex(string hex) =>
        [.. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(b => Convert.ToByte(b, 16))];

    /// <summary>research/patches.json, found from the exe (research/Pes6Research/bin) or the current folder.</summary>
    static string FindPatchesFile()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                foreach (var f in new[] { Path.Combine(d.FullName, "patches.json"), Path.Combine(d.FullName, "research", "patches.json") })
                    if (File.Exists(f)) return f;
        throw new ToolException("patches.json not found: pass -File");
    }
}
