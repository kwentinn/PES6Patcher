namespace PES6Patcher.Core.Patching;

/// <summary>
/// Hands out space for added code in the runs of 0xCC padding between functions of the code
/// section, in address order, 16-byte aligned when possible, else 8-byte aligned, outside the padding Nemesis uses.
/// </summary>
internal sealed class CaveAllocator
{
	// Only large runs (the layout validated in game used runs of 80 bytes or more).
	private const int MinimumRun = 80;

	// Padding nemesis.dll (Nemesis + Majin Editor) writes to while the game runs, as (start, length) in the original
	// pes6.exe, given by its author on Evo-Web (post 4342669). It checks every range is still 0xCC before writing, and
	// leaves everything off if one is not, so none of our code goes there.
	internal static readonly (int Start, int Length)[] Nemesis =
	[
		(0x403500, 106), (0x409630, 116), (0x40AC40, 131), (0x40AD80, 227), (0x40AFD0, 24),
		(0x40B5E0, 129), (0x40B690, 121), (0x40D8A0, 720), (0x40E320, 322), (0x410540, 63),
		(0x411D90, 667), (0x413000, 631), (0x41E1E0, 710), (0x41E540, 250), (0x427B20, 495),
		(0x439BA0, 922), (0x47D5F0, 898), (0x4835E0, 797), (0x4B0B90, 536), (0x5D9560, 511),
	];
	private readonly List<(int Start, int End)> _runs = [];
	private readonly List<int> _cursors = [];

	// Last resort: a section of our own, added only when the padding runs are used up, so exes that
	// fit in them keep the same bytes.
	private const int OverflowSize = 0x4000;
	private readonly PeImage _image;
	private PeSection? _overflow;
	private int _overflowCursor;

	private readonly bool _avoidNemesis;

	public CaveAllocator(PeImage image, bool avoidNemesis = true)
	{
		_image = image;
		_avoidNemesis = avoidNemesis;
		var code = image.Code;
		var bytes = image.Bytes;
		int start = -1;
		int from = code.RawOffset, to = code.RawOffset + code.MappedSize;
		for (int p = from; p <= to; p++)
		{
			bool cc = p < to && bytes[p] == 0xCC;
			if (cc && start < 0) start = p;
			else if (!cc && start >= 0)
			{
				AddRun(start - code.RawOffset + code.Va, p - code.RawOffset + code.Va);
				start = -1;
			}
		}
	}

	/// <summary>Keeps the parts of the run [start, end) outside Nemesis's ranges that are large enough.</summary>
	private void AddRun(int start, int end)
	{
		foreach (var (from, length) in Nemesis.Where(r => _avoidNemesis && r.Start < end && r.Start + r.Length > start).OrderBy(r => r.Start))
		{
			AddPart(start, from);
			start = Math.Max(start, from + length);
		}
		AddPart(start, end);

		void AddPart(int from, int to)
		{
			if (to - from < MinimumRun) return;
			_runs.Add((from, to));
			_cursors.Add(from);
		}
	}

	/// <summary>Address of <paramref name="size"/> free bytes: a padding run, else the added section; 0 when neither fits.</summary>
	public int Allocate(int size)
	{
		for (int i = 0; i < _runs.Count; i++)
		{
			foreach (int alignment in new[] { 16, 8 })
			{
				int at = (_cursors[i] + alignment - 1) / alignment * alignment;
				if (at + size <= _runs[i].End)
				{
					_cursors[i] = at + size;
					return at;
				}
			}
		}
		if (_overflow == null && _image.AddSection(PeImage.OverflowSectionName, OverflowSize) is { } added)
		{
			_overflow = added;
			_overflowCursor = added.Va;
		}
		if (_overflow != null)
		{
			int at = (_overflowCursor + 15) / 16 * 16;
			if (at + size <= _overflow.Va + _overflow.VirtualSize)
			{
				_overflowCursor = at + size;
				return at;
			}
		}
		return 0;
	}
}
