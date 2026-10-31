namespace PES6Patcher.Core.Patching;

/// <summary>A PE section, addresses as virtual addresses (image base included).</summary>
/// <param name="MappedSize">Bytes loaded from the file: the raw data rounded up to whole 4 KB pages
/// (how Windows maps it), but never past the virtual size nor into the next section's data.</param>
internal sealed record PeSection(string Name, int Va, int VirtualSize, int RawOffset, int RawSize, int MappedSize, bool IsCode)
{
    public int End => Va + MappedSize;
    public bool Contains(int va) => va >= Va && va < End;
}

/// <summary>Byte access by virtual address, from the PE section table.</summary>
internal sealed class PeImage
{
    public byte[] Bytes { get; }
    public int ImageBase { get; }
    public IReadOnlyList<PeSection> Sections { get; }

    private readonly int _pe, _sectionTable;

    public PeImage(byte[] bytes)
    {
        Bytes = bytes;
        if (bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z') throw new InvalidDataException("Not a PE file.");
        int pe = BitConverter.ToInt32(bytes, 0x3C);
        if (BitConverter.ToUInt32(bytes, pe) != 0x00004550 || BitConverter.ToUInt16(bytes, pe + 24) != 0x10B)
            throw new InvalidDataException("Not a 32-bit PE file.");
        int count = BitConverter.ToUInt16(bytes, pe + 6);
        int optionalSize = BitConverter.ToUInt16(bytes, pe + 20);
        ImageBase = BitConverter.ToInt32(bytes, pe + 24 + 28);

        int table = pe + 24 + optionalSize;
        _pe = pe;
        _sectionTable = table;
        var raw = new List<(string Name, int Rva, int VirtualSize, int RawOffset, int RawSize, uint Flags)>();
        for (int i = 0; i < count; i++)
        {
            int s = table + 40 * i;
            raw.Add((System.Text.Encoding.ASCII.GetString(bytes, s, 8).TrimEnd('\0'),
                BitConverter.ToInt32(bytes, s + 12), BitConverter.ToInt32(bytes, s + 8),
                BitConverter.ToInt32(bytes, s + 20), BitConverter.ToInt32(bytes, s + 16),
                BitConverter.ToUInt32(bytes, s + 36)));
        }

        static int PageUp(int n) => (n + 0xFFF) & ~0xFFF;
        var sections = new List<PeSection>();
        foreach (var s in raw)
        {
            int nextData = raw.Where(o => o.RawOffset > s.RawOffset).Select(o => o.RawOffset).DefaultIfEmpty(bytes.Length).Min();
            int mapped = Math.Min(Math.Min(PageUp(s.RawSize), PageUp(s.VirtualSize)), Math.Min(nextData, bytes.Length) - s.RawOffset);
            sections.Add(new PeSection(s.Name, ImageBase + s.Rva, s.VirtualSize, s.RawOffset, s.RawSize, Math.Max(mapped, 0),
                (s.Flags & 0x20000020) != 0));   // CNT_CODE or MEM_EXECUTE
        }
        Sections = sections;
    }

    /// <summary>The main code section: the first executable one (.text, sometimes renamed by protections).</summary>
    public PeSection Code => Sections.First(s => s.IsCode);

    public PeSection? Named(string name) => Sections.FirstOrDefault(s => s.Name == name);

    public PeSection? SectionOf(int va) => Sections.FirstOrDefault(s => s.Contains(va));

    public int Offset(int va)
    {
        var s = SectionOf(va) ?? throw new ArgumentOutOfRangeException(nameof(va), $"0x{va:X} is outside the file.");
        return va - s.Va + s.RawOffset;
    }

    public bool Matches(int va, byte[] expected) =>
        SectionOf(va) != null && SectionOf(va + expected.Length - 1) != null &&
        Bytes.AsSpan(Offset(va), expected.Length).SequenceEqual(expected);

    public void Write(int va, byte[] data) => data.CopyTo(Bytes, Offset(va));

    public byte[] Read(int va, int length) => Bytes.AsSpan(Offset(va), length).ToArray();

    public int ReadInt32(int va) => BitConverter.ToInt32(Bytes, Offset(va));

    public float ReadFloat(int va) => SectionOf(va) == null ? float.NaN : BitConverter.ToSingle(Bytes, Offset(va));

    /// <summary>Target of the rel32 call/jmp whose opcode is at <paramref name="va"/>.</summary>
    public int Rel32Target(int va) => va + 5 + ReadInt32(va + 1);

    /// <summary>Address of the import address table slot of <paramref name="dll"/>!<paramref name="function"/>, or 0.</summary>
    public int ImportSlot(string dll, string function)
    {
        int directory = BitConverter.ToInt32(Bytes, _pe + 24 + 104);   // data directory 1: imports
        if (directory == 0 || SectionOf(ImageBase + directory) == null) return 0;
        for (int d = ImageBase + directory; ReadInt32(d + 12) != 0; d += 20)
        {
            if (!string.Equals(ReadString(ImageBase + ReadInt32(d + 12)), dll, StringComparison.OrdinalIgnoreCase)) continue;
            int names = ReadInt32(d) != 0 ? ReadInt32(d) : ReadInt32(d + 16), slots = ReadInt32(d + 16);
            for (int k = 0; ; k++)
            {
                int thunk = ReadInt32(ImageBase + names + 4 * k);
                if (thunk == 0) break;
                if (thunk > 0 && ReadString(ImageBase + thunk + 2) == function) return ImageBase + slots + 4 * k;
            }
        }
        return 0;
    }

    private string ReadString(int va)
    {
        int start = Offset(va), end = start;
        while (end < Bytes.Length && Bytes[end] != 0) end++;
        return System.Text.Encoding.ASCII.GetString(Bytes, start, end - start);
    }

    /// <summary>Adds the write permission to <paramref name="section"/> (section header, IMAGE_SCN_MEM_WRITE).</summary>
    public void MakeWritable(PeSection section)
    {
        int index = Sections.ToList().IndexOf(section);
        int flags = _sectionTable + 40 * index + 36;
        BitConverter.GetBytes(BitConverter.ToUInt32(Bytes, flags) | 0x80000000u).CopyTo(Bytes, flags);
    }
}
