namespace Pes6Research;

/// <summary>An exe file read from disk (the game need not run): its sections, to turn file offsets into virtual addresses and back.</summary>
sealed class PeFile
{
    public sealed record Section(string Name, uint VirtualAddress, uint RawSize, uint RawOffset);

    public byte[] Bytes { get; }
    public uint ImageBase { get; }
    public IReadOnlyList<Section> Sections { get; }

    public PeFile(string path)
    {
        if (!File.Exists(path)) throw new ToolException($"{path}: no such file");
        var b = Bytes = File.ReadAllBytes(path);
        var notExe = new ToolException($"{path}: not a 32-bit exe");
        if (b.Length < 0x40 || b[0] != 'M' || b[1] != 'Z') throw notExe;
        var pe = BitConverter.ToInt32(b, 0x3C);
        if (pe <= 0 || pe > b.Length - 24 - 32 || BitConverter.ToUInt32(b, pe) != 0x4550) throw notExe;
        var count = BitConverter.ToUInt16(b, pe + 6);
        var optional = BitConverter.ToUInt16(b, pe + 20);
        if (BitConverter.ToUInt16(b, pe + 24) != 0x10B) throw notExe; // PE32 optional header (a PE32+ one has the image base elsewhere)
        ImageBase = BitConverter.ToUInt32(b, pe + 24 + 28);
        var first = pe + 24 + optional;
        if ((long)first + 40L * count > b.Length) throw new ToolException($"{path}: the section table is cut short");
        Sections = [.. Enumerable.Range(0, count).Select(i => first + 40 * i).Select(o => new Section(
            System.Text.Encoding.ASCII.GetString(b, o, 8).TrimEnd('\0'),
            BitConverter.ToUInt32(b, o + 12), BitConverter.ToUInt32(b, o + 16), BitConverter.ToUInt32(b, o + 20)))];
    }

    /// <summary>The section holding that file offset, or null (headers, overlay).</summary>
    public Section? SectionAtOffset(long offset) =>
        Sections.FirstOrDefault(s => offset >= s.RawOffset && offset < s.RawOffset + s.RawSize);

    public uint VirtualAddress(Section s, long offset) => (uint)(ImageBase + s.VirtualAddress + offset - s.RawOffset);

    /// <summary>The file offset of a virtual address, or -1 when it is not in the file (outside the sections, or uninitialised data).</summary>
    public long Offset(uint address)
    {
        foreach (var s in Sections)
        {
            var va = ImageBase + s.VirtualAddress;
            if (address >= va && address < va + s.RawSize) return s.RawOffset + address - va;
        }
        return -1;
    }
}
