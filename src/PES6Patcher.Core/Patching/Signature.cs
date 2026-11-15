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

	/// <summary>The longest run of fixed bytes and where it starts: searched with the vectorised IndexOf, then the rest is checked.</summary>
	private readonly byte[] _anchor;
	private readonly int _anchorAt;

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
		int best = 0, bestAt = 0;
		for (int i = 0; i < _bytes.Length;)
		{
			if (_any[i]) { i++; continue; }
			int j = i;
			while (j < _bytes.Length && !_any[j]) j++;
			if (j - i > best) (best, bestAt) = (j - i, i);
			i = j;
		}
		_anchor = _bytes[bestAt..(bestAt + best)];
		_anchorAt = bestAt;
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
		int start = section.RawOffset, end = section.RawOffset + section.MappedSize - _bytes.Length;   // last pattern start
		if (end < start) return hits;
		// the anchor can sit at [start + anchorAt, end + anchorAt]; each of its hits gives a pattern start to check in full
		var area = data.AsSpan(start + _anchorAt, end - start + _anchor.Length);
		for (int from = 0; ;)
		{
			int i = area[from..].IndexOf(_anchor);
			if (i < 0) break;
			int p = start + from + i;   // pattern start: area index + start + anchorAt - anchorAt
			int k = 0;
			while (k < _bytes.Length && (_any[k] || data[p + k] == _bytes[k])) k++;
			if (k == _bytes.Length) hits.Add(p - section.RawOffset + section.Va);
			from += i + 1;
		}
		return hits;
	}
}
