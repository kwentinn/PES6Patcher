namespace Pes6Research;

/// <summary>Reads or overwrites bytes of the running game, code included (memory only, was pes6code.ps1).</summary>
static class CodeCommand
{
    public const string Usage = "code -Address 0x8744F0 [-Length 16] [-Hex \"1C 03 00 00\"]\n" +
                                "                                                 read bytes, or write them (memory only), then read them back";

    public static int Run(Options o, string processName)
    {
        var address = o.Address("Address");
        var length = o.Int("Length", 16);
        var hex = o.String("Hex");
        o.CheckAllUsed();

        using var game = GameProcess.Open(processName, write: hex != null);
        if (hex != null)
        {
            var bytes = ParseHex(hex);
            game.Write(address, bytes);
            length = Math.Max(length, bytes.Length);
        }
        Console.WriteLine($"0x{address:X}: {FormatHex(game.Read(address, length))}");
        return 0;
    }

    /// <summary>"1C 03 00 00" (spaces optional between bytes: "1C030000" works too).</summary>
    public static byte[] ParseHex(string hex)
    {
        var parts = hex.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        try
        {
            return parts.Length == 1 && parts[0].Length > 2 ? Convert.FromHexString(parts[0]) : [.. parts.Select(b => Convert.ToByte(b, 16))];
        }
        catch (FormatException) { throw new ToolException($"'{hex}' is not hex bytes"); }
    }

    public static string FormatHex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).Chunk2();
}

static class HexExtensions
{
    /// <summary>"1C030000" -> "1C 03 00 00".</summary>
    public static string Chunk2(this string hex) =>
        string.Join(' ', Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)));
}
