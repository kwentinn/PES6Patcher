using Pes6Research;

// Some commands print to the console and the tests read it back: run them one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Pes6Research.Tests;

/// <summary>A window of game memory at a 32-bit base address; anything outside it reads as unreadable, as an unmapped page.</summary>
sealed class FakeMemory(uint baseAddress, int size) : IGameMemory
{
    public byte[] Bytes { get; } = new byte[size];
    public int Writes { get; private set; }

    public bool TryRead(uint address, byte[] buffer)
    {
        if (!Inside(address, buffer.Length)) return false;
        Array.Copy(Bytes, address - baseAddress, buffer, 0, buffer.Length);
        return true;
    }

    public void Write(uint address, byte[] bytes)
    {
        if (!Inside(address, bytes.Length)) throw new ToolException($"cannot write 0x{address:X}");
        bytes.CopyTo(Bytes, address - baseAddress);
        Writes++;
    }

    public void Set(uint address, params byte[] bytes) => bytes.CopyTo(Bytes, address - baseAddress);

    public byte[] Get(uint address, int length) => Bytes.AsSpan((int)(address - baseAddress), length).ToArray();

    bool Inside(uint address, int length) => address >= baseAddress && (long)address + length <= (long)baseAddress + Bytes.Length;
}

static class ConsoleOutput
{
    /// <summary>What <paramref name="action"/> prints to the console (standard output and error together), with its return value.</summary>
    public static (T Result, string Output) Capture<T>(Func<T> action)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        var w = new StringWriter();
        Console.SetOut(w); Console.SetError(w);
        try { return (action(), w.ToString()); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
    }
}
