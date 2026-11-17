using PES6Patcher.Core.Patching;

namespace Pes6Research;

/// <summary>
/// The patcher's off-ball pace option, in the running game: its hooks and caves are built by the patcher's own code
/// (ExeLocator and CodeCaves, from the original exe), so what is tried here is the code the patcher writes into an exe. Free
/// space for the two caves is looked for in the game's own memory (the exe the game runs is usually patched already, its caves
/// are somewhere else than in the original one). -Percent can be changed while the game runs: the caves are rewritten in
/// place. The game is not restarted and nothing is written to disk. Follows pace12 / pace13 of patches.json (hand assembled,
/// a fixed share of the distance), which proved the idea in matches.
/// </summary>
static class PaceCommand
{
    public static readonly string[] Switches = ["Revert", "Force"];
    public const string Usage = "pace [-Percent 100] [-Exe PES6.orig.exe] [-Revert] [-Force]\n" +
                                "                                                 the patcher's off-ball pace option, written into the running game";

    public static int Run(Options o, string processName)
    {
        var percent = o.Int("Percent", PatchOptions.DefaultOffBallPace);
        var revert = o.Flag("Revert");
        var force = o.Flag("Force");
        var exe = o.String("Exe") ?? FindOriginal() ?? throw new ToolException("PES6.orig.exe not found (research/reference): pass -Exe");
        o.CheckAllUsed();
        if (percent is < PatchOptions.MinOffBallPace or > PatchOptions.MaxOffBallPace)
            throw new ToolException($"-Percent: from {PatchOptions.MinOffBallPace} to {PatchOptions.MaxOffBallPace}");

        var pe = new PeFile(exe);
        var sites = ExeLocator.LocateOffBallPace(pe.Bytes) ?? throw new ToolException($"{exe}: the off-ball pace places were not found");
        var animationSize = CodeCaves.OffBallPaceAnimation(0, sites, percent).CaveBytes.Length;
        var moverSize = CodeCaves.OffBallPaceMover(0, sites, percent).CaveBytes.Length;
        byte[] Original(int va, int length) => pe.Bytes[(int)pe.Offset((uint)va)..][..length];
        var animationOriginal = Original(sites.Animation, 5);
        var moverOriginal = Original(sites.Mover, 6);

        if (!GameProcess.IsRunning(processName)) throw new ToolException($"{processName} is not running.");
        using var game = GameProcess.Open(processName, write: true);

        // the state of the two entries: the game's own bytes, or jumps into caves of this option (written by this command, or baked
        // into the exe by the patcher: either way, a cave is recognised by its end, the jump back / the original jump)
        var liveAnimation = game.Read((uint)sites.Animation, 5);
        var liveMover = game.Read((uint)sites.Mover, 6);
        var clean = liveAnimation.AsSpan().SequenceEqual(animationOriginal) && liveMover.AsSpan().SequenceEqual(moverOriginal);
        int CaveSize(int cave, bool animationCave)
        {
            var code = game.ReadOrZero((uint)cave, 200);
            for (var i = 40; i + 6 <= code.Length; i++)
                if (animationCave ? code[i] == 0xE9 && cave + i + 5 + BitConverter.ToInt32(code, i + 1) == sites.Animation + 5
                                  : code[i] == 0xFF && code[i + 1] == 0x25 && BitConverter.ToInt32(code, i + 2) == sites.MoverPointer)
                    return i + (animationCave ? 5 : 6);
            return 0;
        }
        int caveAnimation = 0, caveMover = 0, oldAnimationSize = 0, oldMoverSize = 0;
        var ours = liveAnimation[0] == 0xE9 && liveMover[0] == 0xE9 && liveMover[5] == 0x90
            && (caveAnimation = Target(sites.Animation, liveAnimation)) != 0 && (caveMover = Target(sites.Mover, liveMover)) != 0
            && (oldAnimationSize = CaveSize(caveAnimation, true)) != 0 && (oldMoverSize = CaveSize(caveMover, false)) != 0;
        if (!ours && !clean && !force)
            throw new ToolException("the game's bytes at the two entries are neither the original ones nor this option's: another patch? use -Force to write anyway");

        if (revert)
        {
            if (!ours) { Console.WriteLine("off-ball pace is not in the game: nothing to put back"); return 0; }
            // only the jumps go back: the caves stay where they are, unused (wiping them could crash a thread still inside)
            game.Write((uint)sites.Animation, animationOriginal);
            game.Write((uint)sites.Mover, moverOriginal);
            Console.WriteLine("off-ball pace put back as it was (its caves are left in place, unused)");
            return 0;
        }

        // an option already there is rewritten in place only when its caves have the size of the new ones (the percent changed): the
        // other versions are longer or shorter, and are put in free space instead, the old caves being left alone
        if (!ours || oldAnimationSize != animationSize || oldMoverSize != moverSize)
        {
            (caveAnimation, caveMover) = FindFreeSpace(game, pe, animationSize, moverSize);
            Console.WriteLine($"free space in the game: caves at 0x{caveAnimation:X} and 0x{caveMover:X}");
        }
        var animation = CodeCaves.OffBallPaceAnimation(caveAnimation, sites, percent);
        var mover = CodeCaves.OffBallPaceMover(caveMover, sites, percent);
        // the caves first, then the jumps into them
        game.Write((uint)caveAnimation, animation.CaveBytes);
        game.Write((uint)caveMover, mover.CaveBytes);
        game.Write((uint)sites.Animation, animation.EntryBytes);
        game.Write((uint)sites.Mover, mover.EntryBytes);

        var ok = game.Read((uint)caveAnimation, animationSize).AsSpan().SequenceEqual(animation.CaveBytes)
            && game.Read((uint)caveMover, moverSize).AsSpan().SequenceEqual(mover.CaveBytes)
            && game.Read((uint)sites.Animation, 5).AsSpan().SequenceEqual(animation.EntryBytes)
            && game.Read((uint)sites.Mover, 6).AsSpan().SequenceEqual(mover.EntryBytes);
        Console.WriteLine(ok
            ? $"off-ball pace {percent} %: the players placing themselves run at most at 1.4 m/s + {percent} % of the distance to their place"
            : "NOT in place afterwards");
        return ok ? 0 : 1;
    }

    /// <summary>The target of the jmp rel32 at <paramref name="at"/>, or 0 when it is not in the game's code.</summary>
    static int Target(int at, byte[] jump)
    {
        var target = at + 5 + BitConverter.ToInt32(jump, 1);
        return target is > 0x401000 and < 0x800000 ? target : 0;
    }

    /// <summary>
    /// Two 16-aligned places for the caves in the last run of 0xCC padding of the game's code that holds both (the patcher's own
    /// caves fill the runs from the first one, so the last is the likeliest to be free; the bytes are read from the game itself).
    /// </summary>
    static (int Animation, int Mover) FindFreeSpace(GameProcess game, PeFile pe, int animationSize, int moverSize)
    {
        var code = pe.Sections[0];
        var start = pe.ImageBase + code.VirtualAddress;
        var memory = new byte[code.RawSize];
        for (var done = 0; done < memory.Length; done += 0x10000)
        {
            var chunk = new byte[Math.Min(0x10000, memory.Length - done)];
            if (game.TryRead(start + (uint)done, chunk)) chunk.CopyTo(memory, done);
        }
        (int, int) best = (0, 0);
        for (var i = 0; i <= memory.Length;)
        {
            if (i == memory.Length || memory[i] != 0xCC) { i++; continue; }
            var from = i;
            while (i < memory.Length && memory[i] == 0xCC) i++;
            var a = ((int)start + from + 15) / 16 * 16;
            var m = (a + animationSize + 15) / 16 * 16;
            if (m + moverSize <= (int)start + i) best = (a, m);
        }
        return best.Item1 != 0 ? best : throw new ToolException("no run of 0xCC padding is big enough in the game's code");
    }

    static string? FindOriginal()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            {
                var f = Path.Combine(d.FullName, "research", "reference", "PES6.orig.exe");
                if (File.Exists(f)) return f;
                f = Path.Combine(d.FullName, "reference", "PES6.orig.exe");
                if (File.Exists(f)) return f;
            }
        return null;
    }
}
