namespace PES6Patcher.Core.Patching;

/// <summary>
/// Hands out space for added code in the runs of 0xCC padding between functions of the code
/// section, in address order, 16-byte aligned when possible, else 8-byte aligned.
/// </summary>
internal sealed class CaveAllocator
{
    // Only large runs (the layout validated in game used runs of 80 bytes or more).
    private const int MinimumRun = 80;
    private readonly List<(int Start, int End)> _runs = [];
    private readonly List<int> _cursors = [];

    // Last resort: a section of our own, added only when the padding runs are used up, so exes that
    // fit in them keep the same bytes.
    private const int OverflowSize = 0x4000;
    private readonly PeImage _image;
    private PeSection? _overflow;
    private int _overflowCursor;

    public CaveAllocator(PeImage image)
    {
        _image = image;
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
                if (p - start >= MinimumRun)
                {
                    _runs.Add((start - code.RawOffset + code.Va, p - code.RawOffset + code.Va));
                    _cursors.Add(start - code.RawOffset + code.Va);
                }
                start = -1;
            }
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
