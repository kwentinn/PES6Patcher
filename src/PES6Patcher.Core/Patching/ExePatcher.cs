using System.Security.Cryptography;

namespace PES6Patcher.Core.Patching;

/// <summary>Options of a patch build.</summary>
/// <param name="Half">Virtual half-width of the 2D layer: 224 * aspect (398 for 16:9, 796 for 32:9). 256 = original.</param>
/// <param name="FreeDirection">Run along the analog stick's precise angle instead of 8 directions.</param>
public sealed record PatchOptions(int Half, bool FreeDirection);

public sealed class PatchException(IReadOnlyList<string> problems)
    : Exception("PES6.exe ne correspond pas à la version supportée : " + string.Join(" ; ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// Builds a patched copy of the one supported PES6.exe. Every location is checked against the
/// original bytes first; nothing is produced if any check fails.
/// </summary>
public static class ExePatcher
{
    /// <summary>SHA-256 of the only PES6.exe build these addresses were found in.</summary>
    public const string SupportedSha256 = "3FE13850C933C85385F3FB5C45977D2DAA860D1FC03E95E97BE9F4FC24D81382";

    // Shared constants read by the 2D conversions: 256.0 (half of the 512-unit 2D layer) and 1/512.
    private const int HalfConstant = 0x00B8AD44, InverseConstant = 0x00B84978;
    // New private constants, in the zero padding at the end of .rdata.
    private const int NewHalf = 0x00BC3F00, NewInverse = 0x00BC3F04;

    // Instructions of the 2D layer that read 256.0 / 1/512. Readers in gameplay code are left alone
    // (redirecting them made the players slide).
    private static readonly int[] HalfReaders =
    [
        0x78ED90, 0x78F038, 0x78F0D7,
        0x867BE2, 0x867C0B, 0x867C40, 0x867C69,
        0x87AEDC, 0x8B6E9D, 0x8B6EC9,
        0x922EA5, 0x941B98,
    ];
    private static readonly int[] InverseReaders = [0x87AEE4];

    public static string Sha256(byte[] exe) => Convert.ToHexString(SHA256.HashData(exe));

    public static bool IsSupported(byte[] exe) => Sha256(exe) == SupportedSha256;

    /// <summary>Returns a patched copy of <paramref name="original"/>.</summary>
    public static byte[] Build(byte[] original, PatchOptions options)
    {
        if (options.Half is < 256 or > 2048) throw new ArgumentOutOfRangeException(nameof(options), "Demi-largeur hors limites.");

        var image = new PeImage((byte[])original.Clone());
        var plan = new List<(int Va, byte[] Bytes)>();
        var problems = new List<string>();
        int half = options.Half;

        void Require(int va, byte[] expected, string what)
        {
            if (!image.Matches(va, expected)) problems.Add($"{what} (0x{va:X})");
        }
        void Hook(Hook h)
        {
            Require(h.Cave, Enumerable.Repeat((byte)0xCC, h.CaveBytes.Length).ToArray(), $"zone libre : {h.Name}");
            Require(h.Entry, h.Original, $"point d'entrée : {h.Name}");
            plan.Add((h.Cave, h.CaveBytes));
            plan.Add((h.Entry, h.EntryBytes));
        }
        void Retarget(int instruction, int from, int to)
        {
            int at = image.FindOperand(instruction, from);
            if (at < 0) problems.Add($"opérande introuvable (0x{instruction:X})");
            else plan.Add((at, BitConverter.GetBytes(to)));
        }

        // 1. private constants
        Require(NewHalf, new byte[8], "emplacement libre des constantes");
        plan.Add((NewHalf, BitConverter.GetBytes((float)half)));
        plan.Add((NewInverse, BitConverter.GetBytes(1f / (2 * half))));

        // 2. 2D layer width: getter returns 2 * half (layout and clipping) ...
        Require(0x8744F0, [0xB8, 0x00, 0x02, 0x00, 0x00, 0xC3], "largeur 2D");
        plan.Add((0x8744F1, BitConverter.GetBytes(2 * half)));
        // ... but the two calls feeding it to the 3D projection (0xF84064) keep 512
        Require(0x661123, [0xE8, 0xC8, 0x33, 0x21, 0x00], "largeur 3D (0x6610B0)");
        plan.Add((0x661123, [0xB8, 0x00, 0x02, 0x00, 0x00]));
        Require(0xB3F939, [0xE8, 0xB2, 0x4B, 0xD3, 0xFF], "largeur 3D (0xB3F910)");
        plan.Add((0xB3F939, [0xB8, 0x00, 0x02, 0x00, 0x00]));

        // clipping of scrolling lists, player cursor
        Hook(CodeCaves.Clip(half));
        Require(CodeCaves.CursorScaleAddress, new byte[4], "emplacement libre du curseur");
        plan.Add((CodeCaves.CursorScaleAddress, BitConverter.GetBytes((2 * half) / 512f)));
        Hook(CodeCaves.Cursor());

        // 3. 2D conversions read the private constants
        foreach (int ins in HalfReaders) Retarget(ins, HalfConstant, NewHalf);
        foreach (int ins in InverseReaders) Retarget(ins, InverseConstant, NewInverse);

        // 4. free stick direction, and a lower turn threshold (45 -> ~17 degrees) for the running state
        if (options.FreeDirection)
        {
            Hook(CodeCaves.FreeDirection());
            Require(0x5AF3CC, [0x66, 0x83, 0xFE, 0x20], "seuil de virage");
            plan.Add((0x5AF3CF, [0x0C]));
        }

        // 5. initial 2D view rectangles (x, y, w, h)
        byte[] rect = [.. BitConverter.GetBytes(-256f), .. BitConverter.GetBytes(-224f), .. BitConverter.GetBytes(512f), .. BitConverter.GetBytes(448f)];
        foreach (int r in new[] { 0xBEE2D4, 0xBEE2E4 })
        {
            Require(r, rect, "rectangle de vue 2D");
            plan.Add((r, BitConverter.GetBytes((float)-half)));
            plan.Add((r + 8, BitConverter.GetBytes((float)(2 * half))));
        }

        if (problems.Count > 0) throw new PatchException(problems);
        foreach (var (va, bytes) in plan) image.Write(va, bytes);
        return image.Bytes;
    }
}
