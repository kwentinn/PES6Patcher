using PES6Patcher.Core;
using PES6Patcher.Core.Patching;

namespace PES6Patcher.Tests;

public class GameplayPresetsTests
{
    private static readonly InstallSettings Start = new(2560, 1440, false, false, 1.1, CloseDribble: false, PhysicsMod: true, ShotPowerDebug: true);

    [Fact]
    public void Pro_is_every_option_at_the_patcher_defaults()
    {
        var pro = GameplayPresets.Apply(Start, GameplayPreset.Pro);
        Assert.True(pro.FreeDirection && pro.CloseDribble && pro.FreeKick && pro.LoftedAim);
        Assert.Equal(PatchOptions.DefaultStickDeadZone, pro.StickDeadZone);
        Assert.Equal(PatchOptions.DefaultCloseDribbleStrength, pro.CloseDribbleStrength);
        Assert.Equal(PatchOptions.DefaultShotAccuracy, pro.ShotAccuracy);
        Assert.Equal(PatchOptions.DefaultPassLead, pro.PassLead);
        Assert.Equal(PatchOptions.DefaultHeaders, pro.Headers);
        Assert.Equal(PatchOptions.DefaultTackle, pro.Tackle);
    }

    [Fact]
    public void A_preset_keeps_the_display_the_speed_the_physics_mod_and_the_test_keys()
    {
        var hardcore = GameplayPresets.Apply(Start, GameplayPreset.Hardcore);
        Assert.Equal((2560, 1440, 1.1, true, true), (hardcore.Width, hardcore.Height, hardcore.Speed, hardcore.PhysicsMod, hardcore.ShotPowerDebug));
    }

    [Fact]
    public void Each_preset_is_detected_and_a_changed_value_is_custom()
    {
        foreach (var preset in GameplayPresets.All)
            Assert.Equal(preset, GameplayPresets.Detect(GameplayPresets.Apply(Start, preset)));
        Assert.Null(GameplayPresets.Detect(Start));
        Assert.Null(GameplayPresets.Detect(GameplayPresets.Apply(Start, GameplayPreset.Pro) with { ShotPower = 70 }));
    }

    [Fact]
    public void Presets_stay_within_the_options_limits_and_are_buildable()
    {
        foreach (var preset in GameplayPresets.All)
        {
            var s = GameplayPresets.Apply(Start, preset);
            Assert.InRange(s.CloseDribbleStrength, PatchOptions.MinCloseDribbleStrength, PatchOptions.MaxCloseDribbleStrength);
            Assert.InRange(s.SprintTurn, PatchOptions.MinSprintTurn, PatchOptions.MaxSprintTurn);
            Assert.InRange(s.CarrierTurn, PatchOptions.MinCarrierTurn, PatchOptions.MaxCarrierTurn);
            Assert.InRange(s.FreeKickWeight, PatchOptions.MinFreeKickWeight, PatchOptions.MaxFreeKickWeight);
            Assert.InRange(s.ShotPower, PatchOptions.MinShotPower, PatchOptions.MaxShotPower - 1);
            Assert.InRange(s.ShotAccuracy, PatchOptions.MinShotAccuracy, PatchOptions.MaxShotAccuracy - 1);
            Assert.InRange(s.PassLead, PatchOptions.MinPassLead, PatchOptions.MaxPassLead - 1);
            Assert.InRange(s.Headers, PatchOptions.MinHeaders, PatchOptions.MaxHeaders - 1);
        }
    }
}
