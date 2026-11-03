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
/// <param name="FreeKick">Heavier ball after a free kick shot.</param>
/// <param name="FreeKickWeight">Weight added to that ball, in % of the game's ball weight.</param>
/// <param name="FreeKickDebug">Test key: F2 in a match changes the free kick weight.</param>
/// <param name="ShotPower">Power of the human players' shots, in % of the game's (100 = unchanged).</param>
/// <param name="ShotPowerDebug">Test key: F3 in a match changes the shot power.</param>
/// <param name="ShotAccuracy">Aim error of the human players' shots kept, in % of the game's (100 = unchanged).</param>
/// <param name="ShotAccuracyDebug">Test key: F4 in a match changes the aim error kept.</param>
/// <param name="Spread">Players spread across the pitch, in % away from their team's centre (0 = unchanged).</param>
/// <param name="SpreadDebug">Test key: F5 in a match changes the spread.</param>
/// <param name="SpeedGap">Top speed gap between players, in % of the game's (100 = unchanged).</param>
/// <param name="SpeedGapDebug">Test key: F6 in a match changes the speed gap.</param>
/// <param name="SprintTurn">Most the ball may turn per sprint touch for a star dribbler, in degrees (0 = unchanged).</param>
/// <param name="SprintTurnDebug">Test key: F7 in a match changes the sprint turn maximum.</param>
/// <param name="PhysicsMod">Load PityCharly's physics mod DLL (kload.cfg); null = leave as it is.</param>
public sealed record InstallSettings(int Width, int Height, bool BorderlessWindow, bool FreeDirection, double Speed, bool Display = true, bool Gameplay = true, bool CloseDribble = false, bool SkipIntro = false, bool? PhysicsMod = null, bool FreeKick = false,
    int FreeKickWeight = PatchOptions.DefaultFreeKickWeight, bool FreeKickDebug = false, int ShotPower = 100, bool ShotPowerDebug = false,
    int ShotAccuracy = 100, bool ShotAccuracyDebug = false, int Spread = 0, bool SpreadDebug = false, int SpeedGap = 100, bool SpeedGapDebug = false,
    int SprintTurn = 0, bool SprintTurnDebug = false)
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
        bool freeKick = settings.Gameplay && settings.FreeKick;
        int shotPower = settings.Gameplay ? settings.ShotPower : 100, shotAccuracy = settings.Gameplay ? settings.ShotAccuracy : 100;
        int spread = settings.Gameplay ? settings.Spread : 0, speedGap = settings.Gameplay ? settings.SpeedGap : 100,
            sprintTurn = settings.Gameplay ? settings.SprintTurn : 0;
        // without any exe fix, the exe goes back to the original
        byte[] patched = settings.Display || freeDirection || closeDribble || freeKick || shotPower < 100 || shotAccuracy < 100 || spread > 0 || speedGap != 100 || sprintTurn > 0 || settings.SkipIntro
            ? ExePatcher.Build(original, new PatchOptions(settings.Half, freeDirection, settings.Display, closeDribble, settings.SkipIntro, freeKick, settings.FreeKickWeight,
                freeKick && settings.FreeKickDebug, shotPower, shotPower < 100 && settings.ShotPowerDebug,
                shotAccuracy, shotAccuracy < 100 && settings.ShotAccuracyDebug, spread, spread > 0 && settings.SpreadDebug, speedGap, speedGap != 100 && settings.SpeedGapDebug,
                sprintTurn, sprintTurn > 0 && settings.SprintTurnDebug))
            : original;
        string yesNo = Loc.T(freeDirection ? "Common.Yes" : "Common.No");
        log(settings.Display ? Loc.T("Log.ExeBuilt", settings.Half, yesNo) : Loc.T("Log.ExeBuiltNoDisplay", yesNo));
        if (closeDribble) log(Loc.T("Log.CloseDribble"));
        if (freeKick) log(Loc.T("Log.FreeKick", settings.FreeKickWeight));
        if (freeKick && settings.FreeKickDebug) log(Loc.T("Log.FreeKickDebug"));
        if (shotPower < 100) log(Loc.T("Log.Shot", shotPower));
        if (shotPower < 100 && settings.ShotPowerDebug) log(Loc.T("Log.ShotPowerDebug"));
        if (shotAccuracy < 100) log(Loc.T("Log.ShotAccuracy", shotAccuracy));
        if (shotAccuracy < 100 && settings.ShotAccuracyDebug) log(Loc.T("Log.ShotAccuracyDebug"));
        if (spread > 0) log(Loc.T("Log.Spread", spread));
        if (spread > 0 && settings.SpreadDebug) log(Loc.T("Log.SpreadDebug"));
        if (speedGap != 100) log(Loc.T("Log.SpeedGap", speedGap));
        if (speedGap != 100 && settings.SpeedGapDebug) log(Loc.T("Log.SpeedGapDebug"));
        if (sprintTurn > 0) log(Loc.T("Log.SprintTurn", sprintTurn));
        if (sprintTurn > 0 && settings.SprintTurnDebug) log(Loc.T("Log.SprintTurnDebug"));
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
