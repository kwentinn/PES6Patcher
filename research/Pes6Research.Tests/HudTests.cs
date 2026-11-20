using Pes6Research;

namespace Pes6Research.Tests;

public class HudTests
{
    [Fact]
    public void A_state_change_to_a_pass_state_is_logged_with_receiver_buttons_and_gauge()
    {
        var memory = new FakeMemory(0x3BCDC00, 0x14000);   // players, pad, placement words (the ball pointer is outside: unreadable)
        var tracker = new HudTracker();
        var lines = new List<string>();
        tracker.KickLogged += lines.Add;
        var p7 = GameLayout.Player(7);
        memory.Set(p7 + 0x48, 9);                                          // receiver P9, chosen before the state changes
        memory.Set(p7 + 0xB0, 0x00, 0x04, 0x00, 0x00);                     // L1 held
        memory.Set(p7 + 0x4D, 64);
        memory.Set(GameLayout.Carrier, 7);                                 // P7 has the ball
        tracker.Update(memory, 1000);
        memory.Set(p7 + GameLayout.State, 5);
        tracker.Update(memory, 1020);
        tracker.Update(memory, 1700);                                      // the 500 ms window is over
        var line = Assert.Single(lines);
        Assert.Contains("P7", line);
        Assert.Contains("passe courte +L1 -> P9", line);
        Assert.Contains("gauge=64", line);
    }

    [Fact]
    public void Super_shot_buttons_and_no_receiver_make_a_shot()
    {
        Assert.Equal("tir a l'arret +L1+R1", HudTracker.KickKind(4, 0x01000C00, 0));
        Assert.Equal("passe en profondeur", HudTracker.KickKind(6, 0, 3));
    }
}
