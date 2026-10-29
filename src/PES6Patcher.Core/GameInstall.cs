using System.Diagnostics;
using Microsoft.Win32;
using PES6Patcher.Core.Patching;

namespace PES6Patcher.Core;

public enum ExeState { Missing, Original, Patched, Unsupported }

/// <summary>Files of a PES6 installation that the patcher reads and writes.</summary>
public sealed class GameInstall(string folder)
{
    public const string DefaultFolder = @"C:\Program Files (x86)\KONAMI\Pro Evolution Soccer 6";
    private const string BackupSuffix = ".orig";

    public string Folder { get; } = folder;
    public string Exe => Path.Combine(Folder, "PES6.exe");
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
            if (key?.GetValue("installdir") is string dir && File.Exists(Path.Combine(dir, "PES6.exe")))
                return dir.TrimEnd('\\');
        }
        return File.Exists(Path.Combine(DefaultFolder, "PES6.exe")) ? DefaultFolder : null;
    }

    public ExeState GetExeState()
    {
        if (!File.Exists(Exe)) return ExeState.Missing;
        if (ExePatcher.IsSupported(File.ReadAllBytes(Exe))) return ExeState.Original;
        return File.Exists(ExeBackup) && ExePatcher.IsSupported(File.ReadAllBytes(ExeBackup)) ? ExeState.Patched : ExeState.Unsupported;
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
        throw new InvalidOperationException("Aucune copie de PES6.exe dans la version supportée n'a été trouvée.");
    }

    public static bool IsGameRunning() => Process.GetProcessesByName("PES6").Length > 0 || Process.GetProcessesByName("settings").Length > 0;

    internal static string BackupOf(string file) => file + BackupSuffix;
}
