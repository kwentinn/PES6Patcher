using PES6Patcher.Core.Config;
using PES6Patcher.Core.Patching;

namespace PES6Patcher.Core;

/// <summary>What to install.</summary>
/// <param name="Width">Display resolution width (physical pixels).</param>
/// <param name="Height">Display resolution height.</param>
/// <param name="BorderlessWindow">Borderless window covering the screen (needed above 1920 x 1080).</param>
/// <param name="FreeDirection">Analog stick precise direction.</param>
/// <param name="Speed">Game speed factor (1.0 = original).</param>
public sealed record InstallSettings(int Width, int Height, bool BorderlessWindow, bool FreeDirection, double Speed)
{
    /// <summary>Half-width of the 2D layer: the original 448-unit height times the aspect ratio, halved.</summary>
    public int Half => (int)Math.Round(224.0 * Width / Height);
    public float Aspect => (float)Width / Height;
}

public static class Installer
{
    public static void Apply(GameInstall game, InstallSettings settings, Action<string> log)
    {
        if (GameInstall.IsGameRunning()) throw new InvalidOperationException(Loc.T("Error.GameRunning"));
        if (!game.HasKitserver) throw new InvalidOperationException(Loc.T("Error.NoKitserver"));

        // Build first: nothing is touched if the exe does not match.
        byte[] original = game.ReadOriginalExe();
        byte[] patched = ExePatcher.Build(original, new PatchOptions(settings.Half, settings.FreeDirection));
        log(Loc.T("Log.ExeBuilt", settings.Half, Loc.T(settings.FreeDirection ? "Common.Yes" : "Common.No")));

        BackUp(game, original, log);

        File.WriteAllBytes(game.Exe, patched);
        log(Loc.T("Log.ExeInstalled"));

        string kload = File.ReadAllText(game.Kload, TextFiles.Ascii);
        File.WriteAllText(game.Kload, KloadConfig.Apply(kload, settings.Width, settings.Height, settings.BorderlessWindow), TextFiles.Ascii);
        log(Loc.T(settings.BorderlessWindow ? "Log.KloadBorderless" : "Log.Kload", settings.Width, settings.Height));

        if (File.Exists(game.Lodmixer))
        {
            File.WriteAllBytes(game.Lodmixer, LodmixerData.SetAspect(File.ReadAllBytes(game.Lodmixer), settings.Aspect));
            log(Loc.T("Log.Lodmixer", settings.Aspect));
        }
        else log(Loc.T("Log.LodmixerMissing"));

        File.WriteAllText(game.Speeder, SpeederConfig.Create(settings.Speed), TextFiles.Ascii);
        log(Loc.T("Log.Speeder", settings.Speed));

        if (File.Exists(GameInstall.GameSettings))
        {
            File.WriteAllBytes(GameInstall.GameSettings, GameSettingsData.SetResolution(File.ReadAllBytes(GameInstall.GameSettings), settings.Width, settings.Height));
            log(Loc.T("Log.Settings", settings.Width, settings.Height));
        }
        else log(Loc.T("Log.SettingsMissing"));

        log(Loc.T("Log.Done"));
    }

    public static void Restore(GameInstall game, Action<string> log)
    {
        if (GameInstall.IsGameRunning()) throw new InvalidOperationException(Loc.T("Error.GameRunning"));
        int restored = 0;
        foreach (var file in game.ConfigFiles.Prepend(game.Exe))
        {
            string backup = GameInstall.BackupOf(file);
            if (!File.Exists(backup)) continue;
            File.Copy(backup, file, overwrite: true);
            File.Delete(backup);
            log(Loc.T("Log.Restored", Path.GetFileName(file)));
            restored++;
        }
        log(Loc.T(restored == 0 ? "Log.NothingToRestore" : "Log.RestoreDone"));
    }

    /// <summary>Saves the files as they were before the first apply; later applies keep that first copy.</summary>
    private static void BackUp(GameInstall game, byte[] originalExe, Action<string> log)
    {
        if (!File.Exists(game.ExeBackup))
        {
            File.WriteAllBytes(game.ExeBackup, originalExe);
            log(Loc.T("Log.Backup", Path.GetFileName(game.ExeBackup)));
        }
        foreach (var file in game.ConfigFiles)
        {
            string backup = GameInstall.BackupOf(file);
            if (File.Exists(file) && !File.Exists(backup))
            {
                File.Copy(file, backup);
                log(Loc.T("Log.Backup", Path.GetFileName(backup)));
            }
        }
    }
}
