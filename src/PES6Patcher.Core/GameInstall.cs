using System.Diagnostics;
using Microsoft.Win32;
using PES6Patcher.Core.Patching;

namespace PES6Patcher.Core;

public enum ExeState { Missing, Original, Patched, PatchedWithoutBackup, Unsupported }

/// <summary>Files of a PES6 installation that the patcher reads and writes.</summary>
/// <param name="folder">Game folder.</param>
/// <param name="exeName">Game executable: PES6.exe, or the one shipped by a community patch.</param>
public sealed class GameInstall(string folder, string exeName = GameInstall.DefaultExe)
{
    public const string DefaultFolder = @"C:\Program Files (x86)\KONAMI\Pro Evolution Soccer 6";
    public const string DefaultExe = "PES6.exe";
    private const string BackupSuffix = ".orig";

    public string Folder { get; } = folder;
    public string Exe => Path.Combine(Folder, exeName);
    public string ExeBackup => Exe + BackupSuffix;
    public string Kload => Path.Combine(Folder, "kitserver", "kload.cfg");
    public string Lodmixer => Path.Combine(Folder, "kitserver", "lodmixer.dat");
    public string Speeder => Path.Combine(Folder, "kitserver", "speeder.cfg");
    public static string GameSettings => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KONAMI", "Pro Evolution Soccer 6", "settings.dat");

    /// <summary>Files backed up on first apply and put back by restore (the exe is handled separately).</summary>
    public IEnumerable<string> ConfigFiles => [Kload, Lodmixer, Speeder, GameSettings];

    public bool HasKitserver => File.Exists(Kload);

    /// <summary>Install folder from the registry (KONAMIPES6\PES6\installdir), or null.</summary>
    public static string? FindFolder()
    {
        foreach (var path in new[] { @"SOFTWARE\WOW6432Node\KONAMIPES6\PES6", @"SOFTWARE\KONAMIPES6\PES6" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key?.GetValue("installdir") is string dir && File.Exists(Path.Combine(dir, DefaultExe)))
                return dir.TrimEnd('\\');
        }
        return File.Exists(Path.Combine(DefaultFolder, DefaultExe)) ? DefaultFolder : null;
    }

    /// <summary>Game executables of a folder (large .exe files), PES6.exe first.</summary>
    public static IReadOnlyList<string> FindExecutables(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder, "*.exe")
            .Where(f => new FileInfo(f).Length > 8_000_000)
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(n => n.Equals(DefaultExe, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>State of the executable, and the analysis of the one that would be patched.</summary>
    public (ExeState State, ExeReport? Report) Inspect()
    {
        if (!File.Exists(Exe)) return (ExeState.Missing, null);
        var current = ExePatcher.Analyze(File.ReadAllBytes(Exe));
        if (!current.AlreadyPatched) return (current.AnyFix ? ExeState.Original : ExeState.Unsupported, current);

        var backup = File.Exists(ExeBackup) ? ExePatcher.Analyze(File.ReadAllBytes(ExeBackup)) : null;
        return backup is { AlreadyPatched: false, AnyFix: true } ? (ExeState.Patched, backup) : (ExeState.PatchedWithoutBackup, current);
    }

    /// <summary>The unpatched exe: the backup when the installed one is patched, else the installed one.</summary>
    public byte[] ReadOriginalExe()
    {
        foreach (var file in new[] { ExeBackup, Exe })
        {
            if (!File.Exists(file)) continue;
            var bytes = File.ReadAllBytes(file);
            if (ExePatcher.IsSupported(bytes)) return bytes;
        }
        throw new InvalidOperationException(Loc.T("Error.NoOriginalExe"));
    }

    public static bool IsGameRunning() => Process.GetProcessesByName("PES6").Length > 0 || Process.GetProcessesByName("settings").Length > 0;

    internal static string BackupOf(string file) => file + BackupSuffix;
}
