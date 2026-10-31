namespace PES6Patcher.Core.Patching;

/// <summary>
/// Minimal two-pass x86 code builder for the small code caves: raw bytes, labels,
/// short conditional/unconditional jumps and rel32 calls (to an address or a label).
/// </summary>
internal sealed class CodeBuilder
{
    private abstract record Item;
    private sealed record Raw(byte[] Bytes) : Item;
    private sealed record Label(string Name) : Item;
    private sealed record ShortJump(byte Opcode, string Target) : Item;
    private sealed record Call(int? Address, string? Target) : Item;
    private sealed record AbsoluteLabel(byte[] Prefix, string Target) : Item;

    private readonly List<Item> _items = [];
    private readonly int _origin;

    public CodeBuilder(int origin) => _origin = origin;

    public CodeBuilder Bytes(params byte[] bytes) { _items.Add(new Raw(bytes)); return this; }
    public CodeBuilder Bytes(byte[] prefix, int imm32) => Bytes([.. prefix, .. BitConverter.GetBytes(imm32)]);
    public CodeBuilder Mark(string name) { _items.Add(new Label(name)); return this; }
    public CodeBuilder Jump(byte opcode, string target) { _items.Add(new ShortJump(opcode, target)); return this; }
    public CodeBuilder CallTo(int address) { _items.Add(new Call(address, null)); return this; }
    public CodeBuilder CallTo(string label) { _items.Add(new Call(null, label)); return this; }

    /// <summary><paramref name="prefix"/> followed by the absolute address of <paramref name="label"/> (e.g. push offset label).</summary>
    public CodeBuilder AddressOf(byte[] prefix, string label) { _items.Add(new AbsoluteLabel(prefix, label)); return this; }

    public byte[] Build()
    {
        var labels = new Dictionary<string, int>();
        int pos = 0;
        foreach (var item in _items)
        {
            switch (item)
            {
                case Raw r: pos += r.Bytes.Length; break;
                case Label l: labels[l.Name] = pos; break;
                case ShortJump: pos += 2; break;
                case Call: pos += 5; break;
                case AbsoluteLabel a: pos += a.Prefix.Length + 4; break;
            }
        }

        var code = new List<byte>(pos);
        foreach (var item in _items)
        {
            switch (item)
            {
                case Raw r:
                    code.AddRange(r.Bytes);
                    break;
                case ShortJump j:
                    int rel = labels[j.Target] - (code.Count + 2);
                    if (rel is < -128 or > 127) throw new InvalidOperationException($"Short jump to '{j.Target}' out of range.");
                    code.Add(j.Opcode);
                    code.Add((byte)(sbyte)rel);
                    break;
                case Call c:
                    int target = c.Address ?? _origin + labels[c.Target!];
                    code.Add(0xE8);
                    code.AddRange(BitConverter.GetBytes(target - (_origin + code.Count + 4)));
                    break;
                case AbsoluteLabel a:
                    code.AddRange(a.Prefix);
                    code.AddRange(BitConverter.GetBytes(_origin + labels[a.Target]));
                    break;
            }
        }
        return [.. code];
    }

    /// <summary>JMP rel32 from <paramref name="from"/> to <paramref name="to"/>, padded with NOPs.</summary>
    public static byte[] JumpPatch(int from, int to, int totalLength)
    {
        var bytes = new byte[totalLength];
        bytes[0] = 0xE9;
        BitConverter.GetBytes(to - (from + 5)).CopyTo(bytes, 1);
        for (int i = 5; i < totalLength; i++) bytes[i] = 0x90;
        return bytes;
    }
}
