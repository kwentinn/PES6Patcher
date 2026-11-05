using PES6Patcher.Core;

namespace PES6Patcher.Tests;

/// <summary>The settings saved next to a patched exe, read back only while that exe is unchanged.</summary>
public class PatchRecordTests
{
    [Fact]
    public void Settings_come_back_for_the_exe_they_patched_and_not_for_another()
    {
        string folder = Directory.CreateTempSubdirectory("pes6patcher-").FullName;
        try
        {
            var game = new GameInstall(folder);
            byte[] exe = [1, 2, 3, 4];
            File.WriteAllBytes(game.Exe, exe);
            var settings = new InstallSettings(2560, 1080, false, true, 1.1, CloseDribble: true, FreeKick: true, FreeKickWeight: 70,
                ShotPower: 80, ShotAccuracy: 50, ShotAccuracyDebug: true, StickDeadZone: 8, CarrierTurn: 22, PassLead: 60, PhysicsMod: true);

            Assert.Null(PatchRecord.Load(game));                                    // nothing saved yet
            PatchRecord.Save(game, settings, exe);
            Assert.Equal(settings, PatchRecord.Load(game));
            Assert.Contains("StickDeadZone=8", File.ReadAllLines(game.PatchRecordFile));
            Assert.Contains("Speed=1.1", File.ReadAllLines(game.PatchRecordFile));

            File.WriteAllBytes(game.Exe, [1, 2, 3, 5]);                              // another exe: unknown options
            Assert.Null(PatchRecord.Load(game));

            File.WriteAllBytes(game.Exe, exe);
            File.WriteAllText(game.PatchRecordFile, "Width=abc");                    // damaged record: ignored
            Assert.Null(PatchRecord.Load(game));

            PatchRecord.Save(game, settings, exe);                                   // a bad value takes its default
            File.WriteAllText(game.PatchRecordFile, File.ReadAllText(game.PatchRecordFile).Replace("ShotPower=80", "ShotPower=x"));
            Assert.Equal(settings with { ShotPower = 100 }, PatchRecord.Load(game));
            File.WriteAllText(game.PatchRecordFile, File.ReadAllText(game.PatchRecordFile).Replace("Width=2560", "Width=abc"));
            Assert.Null(PatchRecord.Load(game));                                    // a bad required value: unknown options

            PatchRecord.Delete(game);
            Assert.False(File.Exists(game.PatchRecordFile));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
