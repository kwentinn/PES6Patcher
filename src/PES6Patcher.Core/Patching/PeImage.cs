namespace PES6Patcher.Core.Patching;

/// <summary>Byte access by virtual address for the supported PES6.exe layout.</summary>
internal sealed class PeImage(byte[] bytes)
{
    public byte[] Bytes { get; } = bytes;

    // .text is stored at raw 0x400 for RVA 0x1000; .rdata and .data are stored at raw == RVA.
    public static int Offset(int va) => va < 0xB7D000 ? va - 0x400C00 : va - 0x400000;

    public bool Matches(int va, byte[] expected) => Bytes.AsSpan(Offset(va), expected.Length).SequenceEqual(expected);

    public void Write(int va, byte[] data) => data.CopyTo(Bytes, Offset(va));

    /// <summary>Address of the 4-byte absolute operand equal to <paramref name="value"/> inside the instruction at <paramref name="va"/>, or -1.</summary>
    public int FindOperand(int va, int value)
    {
        int o = Offset(va);
        for (int i = 1; i <= 4; i++)
            if (BitConverter.ToInt32(Bytes, o + i) == value) return va + i;
        return -1;
    }
}
