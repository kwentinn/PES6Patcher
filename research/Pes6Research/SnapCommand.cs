namespace Pes6Research;

/// <summary>Saves a range of the game's memory to a file, raw (unreadable pages as zeros), to compare snapshots taken at moments apart.</summary>
static class SnapCommand
{
    public const string Usage = "snap -Address 0x3BC0000 -Length 0x40000 -Out a.bin  raw copy of the game's memory, to compare snapshots";

    public static int Run(Options o, string processName)
    {
        var address = o.Address("Address");
        var length = o.Int("Length", 0x10000);
        var outPath = Path.GetFullPath(o.String("Out") ?? "snap.bin");
        o.CheckAllUsed();
        using var game = GameProcess.Open(processName);
        var bytes = new byte[length];
        for (var at = 0; at < length; at += 0x1000)
        {
            var page = game.ReadOrZero((uint)(address + at), Math.Min(0x1000, length - at));
            page.CopyTo(bytes, at);
        }
        File.WriteAllBytes(outPath, bytes);
        Console.WriteLine($"0x{length:X} bytes from 0x{address:X} -> {outPath}");
        return 0;
    }
}
