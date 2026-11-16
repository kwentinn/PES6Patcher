using System.Diagnostics;
using Microsoft.Win32;
using static Pes6Research.GameLayout;

namespace Pes6Research;

/// <summary>
/// Starts the game and a match nobody plays (CPU against CPU), from the desktop to the kick-off, by pressing the keys of the
/// menus (validated 2026-11-16 on the screens the user captured): language (down: Français, Enter; the option file then loads),
/// title, Match, Exhibition, player settings (down to the controller's row, Enter, right: the controller leaves the home side
/// for no side, Enter, up to OK, Enter), the two teams (the first group, Europe A, then -Home and -Away reached with the arrows from the grid's first team; France
/// against England by default), kits, match settings,
/// line-ups. Refuses when the game already runs: the keys only make sense from its first screen. A screenshot is kept after
/// each step when -Shots gives a folder.
/// </summary>
static class StartMatchCommand
{
    public const string Usage = "startmatch [-Home France] [-Away England] [-Exe PES6.exe] [-Boot 20] [-Shots folder]\n" +
                                "                                                 starts the game and a CPU-vs-CPU match through the menus";

    const int Enter = 28, Up = 328, Down = 336, Right = 333;

    // the national teams of the first group (Europe A), 4 a row, in the order of the game's grid; the cursor starts on the first
    static readonly string[] EuropeA =
    [
        "Austria", "Belgium", "Bulgaria", "Croatia", "Czech", "Denmark", "England", "Finland", "France", "Germany", "Greece", "Hungary",
        "Ireland", "Italy", "Latvia", "Netherlands", "NorthernIreland", "Norway", "Poland", "Portugal",
    ];

    /// <summary>The keys from the grid's first team to that one: down by rows, right by columns.</summary>
    internal static IEnumerable<(int Key, int Wait, string Step)> MovesTo(string team)
    {
        var i = Array.FindIndex(EuropeA, t => t.Equals(team, StringComparison.OrdinalIgnoreCase));
        if (i < 0) throw new ToolException($"unknown team '{team}': one of {string.Join(", ", EuropeA)} (the first group, Europe A; the other groups are not mapped yet)");
        for (var r = 0; r < i / 4; r++) yield return (Down, 500, $"{team}: down");
        for (var c = 0; c < i % 4; c++) yield return (Right, 500, $"{team}: right");
    }

    // key, milliseconds to wait after it, what it does
    static List<(int Key, int Wait, string Step)> Steps(string home, string away) =>
    [
        (Down, 800, "language: Français"), (Enter, 6000, "language chosen, the option file loads"), (Enter, 4000, "option file loaded"),
        (Enter, 4000, "title"), (Enter, 3000, "Match"), (Enter, 3500, "Exhibition"),
        (Down, 800, "player settings: the controller's row"), (Enter, 800, "move the controller"), (Right, 800, "no side"),
        (Enter, 800, "controller placed"), (Up, 800, "OK"), (Enter, 3000, "player settings done"),
        (Enter, 1500, "home group"), .. MovesTo(home), (Enter, 2000, $"home team {home}"),
        (Enter, 1500, "away group"), .. MovesTo(away), (Enter, 3000, $"away team {away}"),
        (Enter, 3500, "kits"), (Enter, 5000, "match settings"),
    ];

    public static int Run(Options o, string processName)
    {
        var exe = o.String("Exe") ?? InstalledExe();
        var boot = o.Int("Boot", 20);
        var shots = o.String("Shots");
        var steps = Steps(o.String("Home") ?? "France", o.String("Away") ?? "England");   // two of the best sides by default
        o.CheckAllUsed();
        if (GameProcess.IsRunning(processName)) throw new ToolException($"{processName} already runs: startmatch drives the menus from the game's first screen. Close it first.");
        if (!File.Exists(exe)) throw new ToolException($"{exe}: not found (use -Exe)");
        if (shots != null) Directory.CreateDirectory(shots);

        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = true })?.Dispose();
        Console.WriteLine($"Started {exe}; waiting {boot} s for the language screen.");
        Thread.Sleep(boot * 1000);

        using var game = GameProcess.Open(processName);
        var n = 0;
        void Press(int key, int wait, string step)
        {
            if (game.HasExited) throw new ToolException("the game closed");
            if (!SkipKey.Press(game, key)) throw new ToolException($"step '{step}': the game did not take the focus, nothing sent. Stopped there.");
            Console.WriteLine($"{++n,2}. {step}");
            Thread.Sleep(wait);
            if (shots != null)
                try { if (game.MainWindow is var w && w != IntPtr.Zero) WindowCommands.Shot(w, Path.Combine(shots, $"start-{n:00}.png")); } catch (Exception) { }
        }
        foreach (var (key, wait, step) in steps) Press(key, wait, step);

        // the line-ups and the entrance go by with Enter; the match is there when both keepers stand near their goals
        for (var i = 0; i < 15 && !InMatch(game); i++) Press(Enter, 6000, "line-ups / entrance");
        if (!InMatch(game)) throw new ToolException("no match after the menus: look at the screenshots (-Shots)");
        var carrier = game.ReadOrZero(Carrier, 1)[0];
        Console.WriteLine($"Match started (carrier {carrier}). No player's buttons are read here: check the first screenshot of the match for a cursor if in doubt.");
        return 0;
    }

    static bool InMatch(GameProcess game) =>
        Math.Abs(BitConverter.ToSingle(game.ReadOrZero(Player(1) + X, 4))) > 8000 &&
        Math.Abs(BitConverter.ToSingle(game.ReadOrZero(Player(12) + X, 4))) > 8000;

    static string InstalledExe()
    {
        using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\KONAMIPES6\PES6");
        var dir = key?.GetValue("installdir") as string ?? throw new ToolException("the game's folder is not in the registry: use -Exe");
        return Path.Combine(dir, "PES6.exe");
    }
}
