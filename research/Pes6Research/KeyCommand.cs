namespace Pes6Research;

/// <summary>Presses one key in the game (brought to the front), as ab does at goals and half-time: to try a key on a screen.</summary>
static class KeyCommand
{
    public const string Usage = "key -Scan 28 [-Times 1] [-Every 1000]                presses a key in the game by scan code (1 = Esc, 28 = Enter)";

    public static int Run(Options o, string processName)
    {
        var scan = o.Int("Scan", 28);
        var times = o.Int("Times", 1);
        var every = o.Int("Every", 1000);
        o.CheckAllUsed();
        using var game = GameProcess.Open(processName);
        for (var i = 0; i < times; i++)
        {
            if (i > 0) Thread.Sleep(every);
            Console.WriteLine(SkipKey.Press(game, scan) ? $"key {scan} sent" : "NOT sent: the game did not take the focus");
        }
        return 0;
    }
}
