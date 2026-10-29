using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PES6Patcher.Core.Config;

/// <summary>Kitserver loader configuration (kitserver\kload.cfg).</summary>
public static partial class KloadConfig
{
    /// <summary>
    /// Sets the render resolution (fullscreen + internal). With <paramref name="borderlessWindow"/>,
    /// also forces a borderless window covering the screen at (0, 0); otherwise comments those lines out.
    /// </summary>
    public static string Apply(string text, int width, int height, bool borderlessWindow)
    {
        text = Set(text, "dx.fullscreen.width", width.ToString(CultureInfo.InvariantCulture));
        text = Set(text, "dx.fullscreen.height", height.ToString(CultureInfo.InvariantCulture));
        text = Set(text, "internal.resolution.width", width.ToString(CultureInfo.InvariantCulture));
        text = Set(text, "internal.resolution.height", height.ToString(CultureInfo.InvariantCulture));

        (string Key, string Value)[] window =
        [
            ("dx.window.x", "0"), ("dx.window.y", "0"),
            ("dx.window.width", width.ToString(CultureInfo.InvariantCulture)),
            ("dx.window.height", height.ToString(CultureInfo.InvariantCulture)),
            ("dx.window.style", "0x96000000"),   // WS_POPUP | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN
            ("dx.window.exstyle", "0x0"),
        ];
        foreach (var (key, value) in window)
            text = borderlessWindow ? Set(text, key, value) : CommentOut(text, key);
        return text;
    }

    /// <summary>Replaces "key = ..." (commented or not); appends it if missing.</summary>
    private static string Set(string text, string key, string value)
    {
        var line = new Regex($@"(?m)^#?{Regex.Escape(key)} = [^\r\n]*");
        if (line.IsMatch(text)) return line.Replace(text, $"{key} = {value}", 1);
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        return text.TrimEnd() + nl + $"{key} = {value}" + nl;
    }

    private static string CommentOut(string text, string key) =>
        new Regex($@"(?m)^{Regex.Escape(key)} = ").Replace(text, $"#{key} = ", 1);
}

/// <summary>Game speed (kitserver\speeder.cfg).</summary>
public static class SpeederConfig
{
    public static string Create(double factor) =>
        "count.factor = " + factor.ToString("0.0#", CultureInfo.InvariantCulture) + "\r\n";

    public static double? Read(string text)
    {
        var m = Regex.Match(text, @"count\.factor\s*=\s*([\d.]+)");
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;
    }
}

/// <summary>LOD Mixer settings (kitserver\lodmixer.dat): manual aspect ratio as a float at offset 41.</summary>
public static class LodmixerData
{
    private const int AspectOffset = 41;

    public static byte[] SetAspect(byte[] data, float aspect)
    {
        if (data.Length < AspectOffset + 4) throw new InvalidDataException(Loc.T("Error.BadLodmixer"));
        var copy = (byte[])data.Clone();
        BitConverter.GetBytes(aspect).CopyTo(copy, AspectOffset);
        return copy;
    }

    public static float GetAspect(byte[] data) => BitConverter.ToSingle(data, AspectOffset);
}

/// <summary>
/// Game settings (Documents\KONAMI\Pro Evolution Soccer 6\settings.dat): display width / height
/// as UInt16 at offsets 4 / 6; offset 2 holds a 16-bit checksum = sum of all bytes from offset 4.
/// settings.exe only offers resolutions up to 1920 x 1080.
/// </summary>
public static class GameSettingsData
{
    public static (int Width, int Height) GetResolution(byte[] data) =>
        (BitConverter.ToUInt16(data, 4), BitConverter.ToUInt16(data, 6));

    public static bool HasValidChecksum(byte[] data) => BitConverter.ToUInt16(data, 2) == Checksum(data);

    public static byte[] SetResolution(byte[] data, int width, int height)
    {
        if (data.Length < 8) throw new InvalidDataException(Loc.T("Error.BadSettings"));
        var copy = (byte[])data.Clone();
        BitConverter.GetBytes((ushort)width).CopyTo(copy, 4);
        BitConverter.GetBytes((ushort)height).CopyTo(copy, 6);
        BitConverter.GetBytes(Checksum(copy)).CopyTo(copy, 2);
        return copy;
    }

    private static ushort Checksum(byte[] data)
    {
        int sum = 0;
        for (int i = 4; i < data.Length; i++) sum += data[i];
        return (ushort)sum;
    }
}

internal static class TextFiles
{
    // The kitserver files are plain ASCII.
    public static readonly Encoding Ascii = Encoding.ASCII;
}
