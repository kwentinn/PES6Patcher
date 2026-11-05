using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PES6Patcher.Core.Config;

namespace PES6Patcher.Core;

/// <summary>
/// The settings of the last apply, saved next to the exe (<see cref="GameInstall.PatchRecordFile"/>) as a flat ini file
/// (one "Name=value" per line, the names of <see cref="InstallSettings"/>), with the SHA-256 of the exe they produced, so
/// the window can show what a patched exe has. A record whose hash no longer matches the exe (replaced, restored, patched by
/// another tool) is ignored; a missing or unreadable setting takes its default.
/// </summary>
public static class PatchRecord
{
    private const string HashKey = "ExeSha256";

    private static readonly ParameterInfo[] Parameters = typeof(InstallSettings).GetConstructors().Single().GetParameters();

    public static void Save(GameInstall game, InstallSettings settings, byte[] exe)
    {
        var text = new StringBuilder();
        text.AppendLine("; PES6 Patcher: the options this exe was patched with").AppendLine($"{HashKey}={Sha256(exe)}");
        foreach (var p in Parameters)
        {
            object? value = typeof(InstallSettings).GetProperty(p.Name!)!.GetValue(settings);
            text.AppendLine($"{p.Name}={(value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value)}");
        }
        File.WriteAllText(game.PatchRecordFile, text.ToString(), TextFiles.Ascii);
    }

    /// <summary>The settings that produced the current exe, or null when unknown.</summary>
    public static InstallSettings? Load(GameInstall game)
    {
        try
        {
            if (!File.Exists(game.PatchRecordFile) || !File.Exists(game.Exe)) return null;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(game.PatchRecordFile, TextFiles.Ascii))
            {
                int equals = line.IndexOf('=');
                if (line.TrimStart().StartsWith(';') || equals <= 0) continue;
                values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
            if (!values.TryGetValue(HashKey, out var hash) || !hash.Equals(Sha256(File.ReadAllBytes(game.Exe)), StringComparison.OrdinalIgnoreCase))
                return null;
            var arguments = new object?[Parameters.Length];
            for (int i = 0; i < Parameters.Length; i++)
            {
                var p = Parameters[i];
                if (values.TryGetValue(p.Name!, out var text) && Parse(text, p.ParameterType) is (true, var value)) arguments[i] = value;
                else if (p.HasDefaultValue) arguments[i] = p.DefaultValue;
                else return null;   // the first parameters (resolution, speed...) have no default: without them the record is of no use
            }
            return (InstallSettings)Activator.CreateInstance(typeof(InstallSettings), arguments)!;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Delete(GameInstall game)
    {
        if (File.Exists(game.PatchRecordFile)) File.Delete(game.PatchRecordFile);
    }

    private static (bool Ok, object? Value) Parse(string text, Type type)
    {
        var inner = Nullable.GetUnderlyingType(type);
        if (inner != null) return text.Length == 0 ? (true, null) : Parse(text, inner);
        if (type == typeof(bool)) return (bool.TryParse(text, out var b), b);
        if (type == typeof(int)) return (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i), i);
        if (type == typeof(double)) return (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d), d);
        return (false, null);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
