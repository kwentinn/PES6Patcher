using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pes6Research;

/// <summary>The running game's memory, read (and written when opened for it) with ReadProcessMemory / WriteProcessMemory: no debugger.</summary>
sealed class GameProcess : IDisposable
{
    const int VmRead = 0x10, VmWrite = 0x20, VmOperation = 0x08, QueryInformation = 0x400;
    const uint PageExecuteReadWrite = 0x40;

    readonly IntPtr handle;
    readonly Process process;

    GameProcess(Process process, IntPtr handle) { this.process = process; this.handle = handle; }

    /// <summary>Opens the first process of that name (PES6 by default), or exits with a message when it is not running.</summary>
    public static GameProcess Open(string name, bool write = false)
    {
        var p = Process.GetProcessesByName(name).FirstOrDefault() ?? throw new ToolException($"{name} is not running.");
        var access = VmRead | QueryInformation | (write ? VmWrite | VmOperation : 0);
        var h = OpenProcess(access, false, p.Id);
        if (h == IntPtr.Zero) throw new ToolException($"cannot open {name} (error {Marshal.GetLastWin32Error()})");
        return new GameProcess(p, h);
    }

    public static bool IsRunning(string name) => Process.GetProcessesByName(name).Length > 0;

    public bool HasExited { get { try { return process.HasExited; } catch { return true; } } }

    /// <summary>The game's window (0 when it has none, as a suspended stand-in).</summary>
    public IntPtr MainWindow { get { try { process.Refresh(); return process.MainWindowHandle; } catch { return IntPtr.Zero; } } }

    /// <summary>Reads <paramref name="buffer"/>.Length bytes; false when the range is not readable (or the game closed).</summary>
    public bool TryRead(uint address, byte[] buffer) =>
        ReadProcessMemory(handle, (IntPtr)address, buffer, buffer.Length, out var n) && n == buffer.Length;

    public byte[] Read(uint address, int length)
    {
        var b = new byte[length];
        if (!TryRead(address, b)) throw new ToolException($"cannot read 0x{address:X} ({length} bytes)");
        return b;
    }

    /// <summary>The bytes, or zeros when the range cannot be read (as the scripts read: a null pointer gives zeros).</summary>
    public byte[] ReadOrZero(uint address, int length)
    {
        var b = new byte[length];
        if (address == 0 || !TryRead(address, b)) Array.Clear(b);
        return b;
    }

    public uint ReadU32(uint address) => BitConverter.ToUInt32(Read(address, 4));
    public byte ReadU8(uint address) => Read(address, 1)[0];

    /// <summary>Writes bytes, code included: the page is made writable for the write, then put back.</summary>
    public void Write(uint address, byte[] bytes)
    {
        VirtualProtectEx(handle, (IntPtr)address, (UIntPtr)bytes.Length, PageExecuteReadWrite, out var old);
        var ok = WriteProcessMemory(handle, (IntPtr)address, bytes, bytes.Length, out var n) && n == bytes.Length;
        VirtualProtectEx(handle, (IntPtr)address, (UIntPtr)bytes.Length, old, out _);
        if (!ok) throw new ToolException($"cannot write 0x{address:X} ({bytes.Length} bytes)");
    }

    public void Dispose() { CloseHandle(handle); process.Dispose(); }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr h, IntPtr address, byte[] buffer, int size, out int read);
    [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr h, IntPtr address, byte[] buffer, int size, out int written);
    [DllImport("kernel32.dll")] static extern bool VirtualProtectEx(IntPtr h, IntPtr address, UIntPtr size, uint protect, out uint old);
}

/// <summary>An expected failure (game not running, bad argument): printed without a stack trace.</summary>
sealed class ToolException(string message) : Exception(message);
