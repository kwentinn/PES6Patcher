using PES6Patcher.Core.Config;
using PES6Patcher.Core.Patching;

namespace PES6Patcher.Core;

/// <summary>What to install.</summary>
/// <param name="Width">Display resolution width (physical pixels).</param>
/// <param name="Height">Display resolution height.</param>
/// <param name="BorderlessWindow">Borderless window covering the screen (needed above 1920 x 1080).</param>
/// <param name="FreeDirection">Analog stick precise direction.</param>
/// <param name="Speed">Game speed factor (1.0 = original).</param>
/// <param name="Display">Widescreen fix and display files (kload.cfg, lodmixer.dat, settings.dat); off = left untouched.</param>
/// <param name="Gameplay">Free direction, close dribbling and game speed (speeder.cfg); off = none is changed.</param>
/// <param name="CloseDribble">Shorter dribble touches.</param>
/// <param name="SkipIntro">Skip the opening movie and the logos.</param>
/// <param name="PhysicsMod">Load PityCharly's physics mod DLL (kload.cfg); null = leave as it is.</param>
public sealed record InstallSettings(int Width, int Height, bool BorderlessWindow, bool FreeDirection, double Speed, bool Display = true, bool Gameplay = true, bool CloseDribble = false, bool SkipIntro = false, bool? PhysicsMod = null)
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

        if (!settings.Display && !settings.Gameplay && !settings.SkipIntro) throw new InvalidOperationException(Loc.T("Error.NothingSelected"));

        // Prepare every file first: nothing is touched if one of them cannot be handled.
        byte[] original = game.ReadOriginalExe();
        var report = ExePatcher.Analyze(original);
        if (!report.Validated) log(Loc.T("Log.Untested", report.Sha256));
        bool freeDirection = settings.Gameplay && settings.FreeDirection;
        bool closeDribble = settings.Gameplay && settings.CloseDribble;
        // without any exe fix, the exe goes back to the original
        byte[] patched = settings.Display || freeDirection || closeDribble || settings.SkipIntro
            ? ExePatcher.Build(original, new PatchOptions(settings.Half, freeDirection, settings.Display, closeDribble, settings.SkipIntro))
            : original;
        string yesNo = Loc.T(freeDirection ? "Common.Yes" : "Common.No");
        log(settings.Display ? Loc.T("Log.ExeBuilt", settings.Half, yesNo) : Loc.T("Log.ExeBuiltNoDisplay", yesNo));
        if (closeDribble) log(Loc.T("Log.CloseDribble"));
        if (settings.SkipIntro) log(Loc.T("Log.SkipIntro"));

        string kloadBefore = File.ReadAllText(game.Kload, TextFiles.Ascii), kload = kloadBefore;
        byte[]? lodmixer = null, gameSettings = null;
        bool lodmixerKnown = false;
        if (settings.Display)
        {
            kload = KloadConfig.Apply(kloadBefore, settings.Width, settings.Height, settings.BorderlessWindow);

            lodmixer = File.Exists(game.Lodmixer) ? File.ReadAllBytes(game.Lodmixer) : null;
            lodmixerKnown = lodmixer != null && LodmixerData.IsRecognized(lodmixer);
            if (lodmixerKnown) lodmixer = LodmixerData.SetAspect(lodmixer!, settings.Aspect);

            gameSettings = File.Exists(GameInstall.GameSettings)
                ? GameSettingsData.SetResolution(File.ReadAllBytes(GameInstall.GameSettings), settings.Width, settings.Height)
                : null;
        }

        BackUp(game, original, log);

        File.WriteAllBytes(game.Exe, patched);
        log(Loc.T("Log.ExeInstalled"));

        // physics mod: only its line in the DLL list, never the rest of kload.cfg
        bool? physicsMod = settings.Gameplay ? settings.PhysicsMod : null;
        if (physicsMod == true && !game.HasPhysicsMod)
        {
            log(Loc.T("Log.PhysicsModMissing", PhysicsMod.FileName));
            physicsMod = null;
        }
        if (physicsMod is bool loaded) kload = KloadConfig.SetDll(kload, PhysicsMod.KloadPath, loaded);
        if (kload != kloadBefore) File.WriteAllText(game.Kload, kload, TextFiles.Ascii);
        if (physicsMod == true)
        {
            log(Loc.T("Log.PhysicsModOn", PhysicsMod.Credit));
            if (!report.Validated) log(Loc.T("Log.PhysicsModUntested"));
        }
        else if (physicsMod == false) log(Loc.T("Log.PhysicsModOff"));

        if (settings.Display)
        {
            log(Loc.T(settings.BorderlessWindow ? "Log.KloadBorderless" : "Log.Kload", settings.Width, settings.Height));
            if (KloadConfig.KeptInternalResolution(kloadBefore, settings.Width, settings.Height) is var (w, h))
                log(Loc.T("Log.KloadInternalKept", w, h));

            if (lodmixer == null) log(Loc.T("Log.LodmixerMissing"));
            else if (!lodmixerKnown) log(Loc.T("Log.LodmixerUnknown", settings.Aspect));
            else
            {
                File.WriteAllBytes(game.Lodmixer, lodmixer);
                log(Loc.T("Log.Lodmixer", settings.Aspect));
            }

            if (gameSettings != null)
            {
                File.WriteAllBytes(GameInstall.GameSettings, gameSettings);
                log(Loc.T("Log.Settings", settings.Width, settings.Height));
            }
            else log(Loc.T("Log.SettingsMissing"));
        }
        else log(Loc.T("Log.DisplayUntouched"));

        if (settings.Gameplay)
        {
            File.WriteAllText(game.Speeder, SpeederConfig.Create(settings.Speed), TextFiles.Ascii);
            log(Loc.T("Log.Speeder", settings.Speed));
        }

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
