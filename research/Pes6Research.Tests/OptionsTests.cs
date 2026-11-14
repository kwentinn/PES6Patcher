using Pes6Research;

namespace Pes6Research.Tests;

public class OptionsTests
{
    [Fact]
    public void Options_read_as_in_powershell_names_ignore_case_and_take_hex()
    {
        var o = new Options(["-Address", "0x3BECC60", "--length", "80", "-FORCE"], "Force");
        Assert.Equal(0x3BECC60u, o.Address("Address"));
        Assert.Equal(80, o.Int("Length", 0));
        Assert.True(o.Flag("Force"));
        o.CheckAllUsed();
    }

    [Fact]
    public void A_switch_does_not_take_the_next_word_as_its_value()
    {
        var o = new Options(["patch", "-Force", "defence-line"], "Force");
        Assert.True(o.Flag("Force"));
        Assert.Equal(new[] { "patch", "defence-line" }, o.Positional);
    }

    [Fact]
    public void Lists_split_on_commas_and_add_up_when_repeated()
    {
        var o = new Options(["-Apply", "a, b", "-Apply", "c"]);
        Assert.Equal(new[] { "a", "b", "c" }, o.List("Apply"));
    }

    [Fact]
    public void A_single_value_keeps_its_commas_and_spaces()
    {
        var o = new Options(["-Out", @"C:\runs\a, b.csv"]);
        Assert.Equal(@"C:\runs\a, b.csv", o.String("Out"));
    }

    [Fact]
    public void A_negative_number_is_a_value_not_an_option()
    {
        var o = new Options(["-Offset", "-12", "-5"]);
        Assert.Equal(-12, o.Int("Offset", 0));
        Assert.Equal(new[] { "-5" }, o.Positional);
    }

    [Fact]
    public void An_option_without_a_value_is_present_but_has_no_value()
    {
        // pes6ab.ps1 took -SpeedPatch '' to keep the game's own speed; PowerShell drops the empty argument
        var o = new Options(["-SpeedPatch", "-Patch", "defence-line"]);
        Assert.True(o.Flag("SpeedPatch"));
        Assert.Null(o.String("SpeedPatch"));
        Assert.Equal("defence-line", o.String("Patch"));
    }

    [Fact]
    public void A_misspelt_option_is_refused_instead_of_ignored()
    {
        var o = new Options(["-Aply", "x"]);
        o.List("Apply");
        Assert.Contains("-Aply", Assert.Throws<ToolException>(o.CheckAllUsed).Message);
    }

    [Fact]
    public void A_value_given_twice_is_refused()
    {
        var o = new Options(["-Seconds", "5", "-Seconds", "6"]);
        Assert.Throws<ToolException>(() => o.Int("Seconds", 0));
    }

    [Theory]
    [InlineData("0x10", 16)]
    [InlineData("0XfF", 255)]
    [InlineData("42", 42)]
    [InlineData("-7", -7)]
    [InlineData("0xFFFFFFFF", -1)]
    public void Numbers_are_decimal_or_hex_with_0x(string text, int value) => Assert.Equal(value, Options.ParseInt(text, "x"));

    [Theory]
    [InlineData("0x")]
    [InlineData("12abc")]
    [InlineData("0x1G")]
    public void A_bad_number_says_which_option(string text) =>
        Assert.Contains("-Length", Assert.Throws<ToolException>(() => Options.ParseInt(text, "Length")).Message);

    [Fact]
    public void A_missing_required_address_is_reported()
    {
        var o = new Options([]);
        Assert.Throws<ToolException>(() => o.Address("Address"));
        Assert.Equal(0x4C9DC0u, o.Address("Address", 0x4C9DC0));
    }
}
