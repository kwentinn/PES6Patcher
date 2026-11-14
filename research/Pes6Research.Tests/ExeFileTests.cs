using Pes6Research;

namespace Pes6Research.Tests;

public sealed class ExeFileTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("pes6r-tests-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    /// <summary>
    /// A minimal PE32 at image base 0x400000: .text (VA 0x1000, file 0x400, 0x200 bytes) and .data (VA 0x2000, file 0x600, 0x200
    /// bytes, then 0x1000 of uninitialised data that is not in the file).
    /// </summary>
    static byte[] MinimalExe()
    {
        var b = new byte[0x800];
        b[0] = (byte)'M'; b[1] = (byte)'Z';
        BitConverter.GetBytes(0x40).CopyTo(b, 0x3C);
        const int pe = 0x40, optional = 0xE0;
        BitConverter.GetBytes(0x4550u).CopyTo(b, pe);
        BitConverter.GetBytes((ushort)0x14C).CopyTo(b, pe + 4);   // i386
        BitConverter.GetBytes((ushort)2).CopyTo(b, pe + 6);       // sections
        BitConverter.GetBytes((ushort)optional).CopyTo(b, pe + 20);
        BitConverter.GetBytes((ushort)0x10B).CopyTo(b, pe + 24);  // PE32
        BitConverter.GetBytes(0x400000u).CopyTo(b, pe + 24 + 28);
        void Section(int i, string name, uint va, uint virtualSize, uint rawSize, uint raw)
        {
            var o = pe + 24 + optional + 40 * i;
            System.Text.Encoding.ASCII.GetBytes(name).CopyTo(b, o);
            BitConverter.GetBytes(virtualSize).CopyTo(b, o + 8);
            BitConverter.GetBytes(va).CopyTo(b, o + 12);
            BitConverter.GetBytes(rawSize).CopyTo(b, o + 16);
            BitConverter.GetBytes(raw).CopyTo(b, o + 20);
        }
        Section(0, ".text", 0x1000, 0x200, 0x200, 0x400);
        Section(1, ".data", 0x2000, 0x1200, 0x200, 0x600);
        return b;
    }

    string Write(byte[] bytes, string name = "test.exe")
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Virtual_addresses_and_file_offsets_convert_both_ways()
    {
        var pe = new PeFile(Write(MinimalExe()));
        Assert.Equal(0x400000u, pe.ImageBase);
        Assert.Equal(new[] { ".text", ".data" }, pe.Sections.Select(s => s.Name));
        Assert.Equal(0x410, pe.Offset(0x401010));
        Assert.Equal(0x600, pe.Offset(0x402000));
        Assert.Equal(-1, pe.Offset(0x402300)); // uninitialised data: not in the file
        Assert.Equal(-1, pe.Offset(0x400010)); // the headers
        var s = pe.SectionAtOffset(0x5FF)!;
        Assert.Equal(".text", s.Name);
        Assert.Equal(0x4011FFu, pe.VirtualAddress(s, 0x5FF));
        Assert.Null(pe.SectionAtOffset(0x100));
    }

    [Theory]
    [InlineData(0x10)]  // shorter than a DOS header
    [InlineData(0x800)]
    public void A_file_that_is_not_an_exe_is_refused(int length)
    {
        var bytes = new byte[length];
        Assert.Throws<ToolException>(() => new PeFile(Write(bytes)));
    }

    [Fact]
    public void A_section_table_cut_short_is_refused()
    {
        var bytes = MinimalExe().AsSpan(0, 0x40 + 24 + 0xE0 + 40).ToArray(); // the first section only
        Assert.Contains("cut short", Assert.Throws<ToolException>(() => new PeFile(Write(bytes))).Message);
    }

    [Fact]
    public void Findref_lists_the_places_in_sections_with_the_bytes_around()
    {
        var b = MinimalExe();
        BitConverter.GetBytes(0x3BE09CCu).CopyTo(b, 0x450); // in .text
        BitConverter.GetBytes(0x3BE09CCu).CopyTo(b, 0x620); // in .data
        BitConverter.GetBytes(0x3BE09CCu).CopyTo(b, 0x10);  // in the headers: left out
        b[0x44F] = 0xA1; // mov eax, [0x3BE09CC]
        var exe = Write(b);
        var (code, output) = ConsoleOutput.Capture(() => ExeCommands.FindRef(new Options(["-Exe", exe, "-Values", "0x3BE09CC"]), "PES6"));
        Assert.Equal(0, code);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("== 0x3BE09CC", lines[0]);
        Assert.Equal(".text  0x401050: 00 00 00 00 00 00 00 A1 [CC 09 BE 03] 00 00 00 00 00 00", lines[1]);
        Assert.StartsWith(".data  0x402020:", lines[2]);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void Readva_reads_float_int_and_bytes_or_says_not_in_the_file()
    {
        var b = MinimalExe();
        BitConverter.GetBytes(1.5f).CopyTo(b, 0x610);
        var exe = Write(b);
        var (_, output) = ConsoleOutput.Capture(() => ExeCommands.ReadVa(new Options(["-Exe", exe, "-Addresses", "0x402010,0x402300", "-Length", "4"]), "PES6"));
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("0x402010: f=1.5 i=1069547520 bytes=00 00 C0 3F", lines[0]);
        Assert.Equal("0x402300: not in the file", lines[1]);
    }
}
