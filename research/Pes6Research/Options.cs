using System.Globalization;

namespace Pes6Research;

/// <summary>
/// PowerShell-like options, so the old command lines still read the same: <c>-Address 0x3BECC60 -Length 0x50</c>,
/// switches (<c>-Force</c>), lists (<c>-Apply a,b</c> or <c>-Apply a -Apply b</c>). Names ignore case; <c>--name</c> works too.
/// Values that do not follow an option are positional.
/// </summary>
sealed class Options
{
    readonly Dictionary<string, List<string>> values = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Positional { get; } = [];

    public Options(IEnumerable<string> args, params string[] switches)
    {
        var isSwitch = new HashSet<string>(switches, StringComparer.OrdinalIgnoreCase);
        string? pending = null;
        foreach (var a in args)
        {
            if (a.Length > 1 && a[0] == '-' && !char.IsDigit(a[1]))
            {
                if (pending != null) Add(pending, null);
                var name = a.TrimStart('-');
                if (isSwitch.Contains(name)) { Add(name, null); pending = null; } else pending = name;
            }
            else if (pending != null) { Add(pending, a); pending = null; }
            else Positional.Add(a);
        }
        if (pending != null) Add(pending, null);
    }

    // every value given for the option, as typed (a switch or an option without a value adds none)
    void Add(string name, string? value)
    {
        if (!values.TryGetValue(name, out var list)) values[name] = list = [];
        if (!string.IsNullOrEmpty(value)) list.Add(value);
    }

    public bool Flag(string name) { used.Add(name); return values.ContainsKey(name); }

    /// <summary>The value as typed (a path may hold commas), or null; refuses an option given twice.</summary>
    public string? String(string name)
    {
        used.Add(name);
        if (!values.TryGetValue(name, out var l) || l.Count == 0) return null;
        return l.Count == 1 ? l[0] : throw new ToolException($"-{name} is given {l.Count} times");
    }

    /// <summary>Every value, split on commas: <c>-Apply a,b -Apply c</c> gives a, b, c.</summary>
    public List<string> List(string name)
    {
        used.Add(name);
        return values.TryGetValue(name, out var l)
            ? [.. l.SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))]
            : [];
    }

    public int Int(string name, int fallback) => String(name) is { } s ? ParseInt(s, name) : fallback;

    public uint Address(string name) => String(name) is { } s ? (uint)ParseInt(s, name) : throw new ToolException($"-{name} is required");

    public uint Address(string name, uint fallback) => String(name) is { } s ? (uint)ParseInt(s, name) : fallback;

    /// <summary>Decimal, or hex with 0x (as PowerShell reads them).</summary>
    public static int ParseInt(string s, string what)
    {
        var ok = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            : int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        return ok ? v : throw new ToolException($"-{what}: '{s}' is not a number");
    }

    /// <summary>Refuses a misspelt option instead of ignoring it silently. Call after reading every option.</summary>
    public void CheckAllUsed()
    {
        var unknown = values.Keys.Where(k => !used.Contains(k)).ToList();
        if (unknown.Count > 0) throw new ToolException($"unknown option: -{string.Join(", -", unknown)}");
    }
}
