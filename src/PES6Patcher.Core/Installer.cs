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
        if (GameInstall.IsGameRunning()) throw new InvalidOperationException("Fermer PES6 et settings.exe avant d'appliquer.");
        if (!game.HasKitserver) throw new InvalidOperationException("Kitserver est introuvable : il est nécessaire pour la résolution et le format 3D.");

        // Build first: nothing is touched if the exe does not match.
        byte[] original = game.ReadOriginalExe();
        byte[] patched = ExePatcher.Build(original, new PatchOptions(settings.Half, settings.FreeDirection));
        log($"Exe patché construit (demi-largeur 2D {settings.Half}, directions libres : {(settings.FreeDirection ? "oui" : "non")}).");

        BackUp(game, original, log);

        File.WriteAllBytes(game.Exe, patched);
        log("PES6.exe installé.");

        string kload = File.ReadAllText(game.Kload, TextFiles.Ascii);
        File.WriteAllText(game.Kload, KloadConfig.Apply(kload, settings.Width, settings.Height, settings.BorderlessWindow), TextFiles.Ascii);
        log($"kload.cfg : rendu {settings.Width} × {settings.Height}{(settings.BorderlessWindow ? ", fenêtre sans bordure" : "")}.");

        if (File.Exists(game.Lodmixer))
        {
            File.WriteAllBytes(game.Lodmixer, LodmixerData.SetAspect(File.ReadAllBytes(game.Lodmixer), settings.Aspect));
            log($"lodmixer.dat : format 3D {settings.Aspect:0.#####}.");
        }
        else log("lodmixer.dat absent : lancer lodcfg.exe de Kitserver une fois, puis réappliquer.");

        File.WriteAllText(game.Speeder, SpeederConfig.Create(settings.Speed), TextFiles.Ascii);
        log($"speeder.cfg : vitesse {settings.Speed:0.0#}.");

        if (File.Exists(GameInstall.GameSettings))
        {
            File.WriteAllBytes(GameInstall.GameSettings, GameSettingsData.SetResolution(File.ReadAllBytes(GameInstall.GameSettings), settings.Width, settings.Height));
            log($"settings.dat : résolution du jeu {settings.Width} × {settings.Height}.");
        }
        else log("settings.dat absent : lancer settings.exe une fois, puis réappliquer.");

        log("Terminé.");
    }

    public static void Restore(GameInstall game, Action<string> log)
    {
        if (GameInstall.IsGameRunning()) throw new InvalidOperationException("Fermer PES6 et settings.exe avant de restaurer.");
        int restored = 0;
        foreach (var file in game.ConfigFiles.Prepend(game.Exe))
        {
            string backup = GameInstall.BackupOf(file);
            if (!File.Exists(backup)) continue;
            File.Copy(backup, file, overwrite: true);
            File.Delete(backup);
            log($"{Path.GetFileName(file)} restauré.");
            restored++;
        }
        log(restored == 0 ? "Rien à restaurer." : "Restauration terminée.");
    }

    /// <summary>Saves the files as they were before the first apply; later applies keep that first copy.</summary>
    private static void BackUp(GameInstall game, byte[] originalExe, Action<string> log)
    {
        if (!File.Exists(game.ExeBackup))
        {
            File.WriteAllBytes(game.ExeBackup, originalExe);
            log("Sauvegarde : PES6.exe.orig.");
        }
        foreach (var file in game.ConfigFiles)
        {
            string backup = GameInstall.BackupOf(file);
            if (File.Exists(file) && !File.Exists(backup))
            {
                File.Copy(file, backup);
                log($"Sauvegarde : {Path.GetFileName(backup)}.");
            }
        }
    }
}
