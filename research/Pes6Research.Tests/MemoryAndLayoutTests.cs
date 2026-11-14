using Pes6Research;

namespace Pes6Research.Tests;

public class MemoryAndLayoutTests
{
    [Theory]
    [InlineData("1C 03 00 00", new byte[] { 0x1C, 0x03, 0x00, 0x00 })]
    [InlineData("1c030000", new byte[] { 0x1C, 0x03, 0x00, 0x00 })]
    [InlineData("A0, 00", new byte[] { 0xA0, 0x00 })]
    [InlineData("  3C ", new byte[] { 0x3C })]
    public void Hex_bytes_are_read_spaced_or_not(string text, byte[] bytes) => Assert.Equal(bytes, CodeCommand.ParseHex(text));

    [Theory]
    [InlineData("1C0")]
    [InlineData("ZZ")]
    [InlineData("1C 300")]
    public void Bad_hex_is_refused(string text) => Assert.Throws<ToolException>(() => CodeCommand.ParseHex(text));

    [Fact]
    public void Hex_is_printed_as_spaced_pairs() => Assert.Equal("00 F6 FF FF", CodeCommand.FormatHex(new byte[] { 0x00, 0xF6, 0xFF, 0xFF }));

    [Fact]
    public void Reads_outside_the_memory_give_zeros_or_an_error()
    {
        var m = new FakeMemory(0x1000, 0x100);
        m.Set(0x1010, 1, 2, 3, 4);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, m.Read(0x1010, 4));
        Assert.Equal(new byte[4], m.ReadOrZero(0x2000, 4));
        Assert.Equal(new byte[4], m.ReadOrZero(0, 4)); // a null pointer: zeros, as the scripts read
        Assert.Throws<ToolException>(() => m.Read(0x10FE, 4));
    }

    [Fact]
    public void The_ball_is_read_through_its_pointer_and_is_zero_before_a_match()
    {
        var m = new FakeMemory(0xBC0000, 0x20000);
        Assert.Equal((0f, 0f, 0f), GameLayout.Ball(m)); // null pointer
        m.Set(GameLayout.BallPointer, BitConverter.GetBytes(0xBD0000u));
        m.Set(0xBD0010, [.. BitConverter.GetBytes(-1234.5f), .. BitConverter.GetBytes(77f), .. BitConverter.GetBytes(0.5f)]);
        Assert.Equal((-1234.5f, 77f, 0.5f), GameLayout.Ball(m));
        Assert.Equal(0xBD0000u, GameLayout.BallAddress(m));
    }

    [Fact]
    public void Players_are_0x240_bytes_apart_from_id_1()
    {
        Assert.Equal(0x3BDC980u + 0x240, GameLayout.Player(1));
        Assert.Equal(0x3BDC980u + 12 * 0x240, GameLayout.Player(12));
    }

    [Theory]
    [InlineData(0x0000, 0, 0.0)]
    [InlineData(0x4000, 0, 90.0)]
    [InlineData(0x1000, 0, 22.0)] // 22.5 rounds to even, as [math]::Round did in the scripts
    [InlineData(0x3000, 0, 68.0)] // 67.5 rounds to even
    [InlineData(0x1000, 1, 22.5)]
    [InlineData(0xFFFF, 0, 360.0)]
    public void Game_angles_become_degrees_rounded_as_the_scripts_did(int angle, int decimals, double degrees) =>
        Assert.Equal(degrees, GameLayout.Degrees((ushort)angle, decimals));

    [Fact]
    public void Debug_register_7_enables_one_execute_breakpoint_per_address()
    {
        Assert.Equal(0x1u, HardwareBreakpoints.ExecuteDr7(1));
        Assert.Equal(0x55u, HardwareBreakpoints.ExecuteDr7(4));
        Assert.Equal(0x00050001u, HardwareBreakpoints.DataDr7(read: false)); // write, 2 bytes
        Assert.Equal(0x000F0001u, HardwareBreakpoints.DataDr7(read: true));  // read/write, 4 bytes
    }
}
