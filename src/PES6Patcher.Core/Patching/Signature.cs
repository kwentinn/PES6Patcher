using System.Globalization;

namespace PES6Patcher.Core.Patching;

/// <summary>
/// Byte pattern such as "D8 25 ?? ?? ?? ?? D9 59": "??" matches any byte. Absolute addresses and
/// call targets are wildcards, so the same code is found in builds where it lives elsewhere.
/// </summary>
internal sealed class Signature
{
    private readonly byte[] _bytes;
    private readonly bool[] _any;

    public Signature(string pattern)
    {
        var tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _bytes = new byte[tokens.Length];
        _any = new bool[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "??") _any[i] = true;
            else _bytes[i] = byte.Parse(tokens[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        if (_any[0]) throw new ArgumentException("A signature cannot start with a wildcard.", nameof(pattern));
    }

    public int Length => _bytes.Length;

    /// <summary>True when the pattern matches at <paramref name="va"/>.</summary>
    public bool IsAt(PeImage image, int va)
    {
        if (image.SectionOf(va) is not { } s || va + _bytes.Length > s.End) return false;
        int o = image.Offset(va);
        for (int k = 0; k < _bytes.Length; k++)
            if (!_any[k] && image.Bytes[o + k] != _bytes[k]) return false;
        return true;
    }

    /// <summary>Virtual addresses of every match inside <paramref name="section"/>.</summary>
    public List<int> FindAll(PeImage image, PeSection section)
    {
        var hits = new List<int>();
        var data = image.Bytes;
        int start = section.RawOffset, end = section.RawOffset + section.MappedSize - _bytes.Length;
        byte first = _bytes[0];
        for (int p = start; p <= end; p++)
        {
            if (data[p] != first) continue;
            int k = 1;
            while (k < _bytes.Length && (_any[k] || data[p + k] == _bytes[k])) k++;
            if (k == _bytes.Length) hits.Add(p - section.RawOffset + section.Va);
        }
        return hits;
    }
}
