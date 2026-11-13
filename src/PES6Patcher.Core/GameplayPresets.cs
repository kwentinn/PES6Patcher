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
        // the super shot follows the same axis: faster and flatter it goes in every time, slower and higher it has to
        // be placed (the game's own hardest shots reach about 30700, and its shots leave at a height of 13 to 35). The
        // dipping ball of a star follows it too: Normal gives it to more shooters and dips less, Hardcore the opposite
        var (closeDribble, sprintTurn, carrierTurn, freeKick, shotPower, shotAccuracy, passLead, headers, superShot, superShotSpeed, superShotWeight, superShotStar) = preset switch
        {
            GameplayPreset.Normal => (150, 90, 22, 70, 75, 30, 50, 60, 40, 40000, 45, 85),
            GameplayPreset.Hardcore => (100, 45, 11, 30, 90, 75, 80, 80, 52, 32000, 70, 95),
            _ => (PatchOptions.DefaultCloseDribbleStrength, PatchOptions.DefaultSprintTurn, PatchOptions.DefaultCarrierTurn,
                PatchOptions.DefaultFreeKickWeight, PatchOptions.DefaultShotPower, PatchOptions.DefaultShotAccuracy, PatchOptions.DefaultPassLead, PatchOptions.DefaultHeaders,
                PatchOptions.DefaultSuperShot, PatchOptions.DefaultSuperShotSpeed, PatchOptions.DefaultSuperShotWeight, PatchOptions.DefaultSuperShotStar),
        };
        return settings with
        {
            FreeDirection = true, StickDeadZone = PatchOptions.DefaultStickDeadZone,
            CloseDribble = true, CloseDribbleStrength = closeDribble, SprintTurn = sprintTurn, CarrierTurn = carrierTurn,
            FreeKick = true, FreeKickWeight = freeKick, ShotPower = shotPower, ShotAccuracy = shotAccuracy, LobPower = PatchOptions.DefaultLobPower,
            LoftedAim = true, PassLead = passLead, Headers = headers, SuperShot = superShot, SuperShotSpeed = superShotSpeed, SuperShotWeight = superShotWeight, SuperShotStar = superShotStar,
            Spread = PatchOptions.DefaultSpread, Tackle = PatchOptions.DefaultTackle, SpeedGap = PatchOptions.DefaultSpeedGap, AccelerationGap = true,
        };
    }

    /// <summary>The preset whose gameplay options are those of <paramref name="settings"/>, or null (custom settings).</summary>
    public static GameplayPreset? Detect(InstallSettings settings) =>
        All.Cast<GameplayPreset?>().FirstOrDefault(p => Apply(settings, p!.Value) == settings);
}
