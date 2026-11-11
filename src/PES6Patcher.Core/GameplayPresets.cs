using PES6Patcher.Core.Patching;

namespace PES6Patcher.Core;

/// <summary>Ready-made gameplay settings for the window's simple mode.</summary>
public enum GameplayPreset
{
    /// <summary>More help: more accurate shots, the ball closer to the feet.</summary>
    Normal,

    /// <summary>The patcher's defaults, tested in matches.</summary>
    Pro,

    /// <summary>Less help: more left to the player.</summary>
    Hardcore,
}

public static class GameplayPresets
{
    public static readonly GameplayPreset[] All = [GameplayPreset.Normal, GameplayPreset.Pro, GameplayPreset.Hardcore];

    /// <summary>
    /// <paramref name="settings"/> with the gameplay options of <paramref name="preset"/>: every option on, at the
    /// preset's values. The display, the intro, the physics mod, the game speed and the test keys are kept.
    /// </summary>
    public static InstallSettings Apply(InstallSettings settings, GameplayPreset preset)
    {
        var (closeDribble, sprintTurn, carrierTurn, freeKick, shotPower, shotAccuracy, passLead) = preset switch
        {
            GameplayPreset.Normal => (150, 90, 22, 70, 75, 30, 50),
            GameplayPreset.Hardcore => (100, 45, 11, 30, 90, 75, 80),
            _ => (PatchOptions.DefaultCloseDribbleStrength, PatchOptions.DefaultSprintTurn, PatchOptions.DefaultCarrierTurn,
                PatchOptions.DefaultFreeKickWeight, PatchOptions.DefaultShotPower, PatchOptions.DefaultShotAccuracy, PatchOptions.DefaultPassLead),
        };
        return settings with
        {
            FreeDirection = true, StickDeadZone = PatchOptions.DefaultStickDeadZone,
            CloseDribble = true, CloseDribbleStrength = closeDribble, SprintTurn = sprintTurn, CarrierTurn = carrierTurn,
            FreeKick = true, FreeKickWeight = freeKick, ShotPower = shotPower, ShotAccuracy = shotAccuracy, LobPower = PatchOptions.DefaultLobPower,
            LoftedAim = true, PassLead = passLead,
            Spread = PatchOptions.DefaultSpread, Tackle = PatchOptions.DefaultTackle, SpeedGap = PatchOptions.DefaultSpeedGap, AccelerationGap = true,
        };
    }

    /// <summary>The preset whose gameplay options are those of <paramref name="settings"/>, or null (custom settings).</summary>
    public static GameplayPreset? Detect(InstallSettings settings) =>
        All.Cast<GameplayPreset?>().FirstOrDefault(p => Apply(settings, p!.Value) == settings);
}
