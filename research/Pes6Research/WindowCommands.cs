using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Pes6Research;

/// <summary>
/// The game's (or another process's) windows, in physical pixels (the tool is made per-monitor DPI aware first): pes6window.ps1
/// (geometry and DPI handling) and capture-window.ps1 (a screenshot, even behind other windows).
/// </summary>
static class WindowCommands
{
    public const string WindowUsage = "window                                           geometry and DPI handling of the game's windows";
    public const string CaptureUsage = "capture [-Out shot.png]                          screenshot of the main window, even behind others (-Process PES6Patcher: the app)";

    public static int Window(Options o, string processName)
    {
        o.CheckAllUsed();
        PerMonitorDpi();
        var p = Process.GetProcessesByName(processName).FirstOrDefault() ?? throw new ToolException($"{processName} is not running.");
        var found = new List<IntPtr>();
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (pid == p.Id && IsWindowVisible(h)) found.Add(h);
            return true;
        }, IntPtr.Zero);
        foreach (var h in found)
        {
            var title = new StringBuilder(256);
            GetWindowText(h, title, 256);
            GetWindowRect(h, out var w); GetClientRect(h, out var c);
            DwmGetWindowAttribute(h, DwmExtendedFrameBounds, out var d, 16);
            var awareness = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(h));
            Console.WriteLine($"fenetre '{title}'");
            Console.WriteLine($"  rect (physique DWM) : {d.L},{d.T} -> {d.R},{d.B}  ({d.R - d.L} x {d.B - d.T})");
            Console.WriteLine($"  rect (GetWindowRect): {w.L},{w.T} -> {w.R},{w.B}  ({w.R - w.L} x {w.B - w.T})");
            Console.WriteLine($"  zone client         : {c.R - c.L} x {c.B - c.T}");
            Console.WriteLine($"  DPI fenetre = {GetDpiForWindow(h)} ; prise en compte DPI = {awareness} (0 = non, 1 = systeme, 2 = par ecran)");
        }
        if (found.Count == 0) Console.WriteLine("no visible window");
        return 0;
    }

    public static int Capture(Options o, string processName)
    {
        var outPath = Path.GetFullPath(o.String("Out") ?? "shot.png");
        o.CheckAllUsed();
        PerMonitorDpi();
        var p = Process.GetProcessesByName(processName).FirstOrDefault() ?? throw new ToolException($"{processName} is not running.");
        var h = p.MainWindowHandle;
        if (h == IntPtr.Zero) throw new ToolException($"{processName} has no main window");
        GetWindowRect(h, out var r);
        int width = r.R - r.L, height = r.B - r.T;
        if (width <= 0 || height <= 0) throw new ToolException("the window has no size (minimised?)");
        using var bmp = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            PrintWindow(h, hdc, RenderFullContent);
            g.ReleaseHdc(hdc);
        }
        bmp.Save(outPath, ImageFormat.Png);
        Console.WriteLine($"{width} x {height} -> {outPath}");
        return 0;
    }

    /// <summary>Per-monitor v2: the sizes read are physical pixels, not scaled ones.</summary>
    static void PerMonitorDpi() => SetProcessDpiAwarenessContext(-4);

    const int DwmExtendedFrameBounds = 9;
    const uint RenderFullContent = 2;

    [StructLayout(LayoutKind.Sequential)] struct Rect { public int L, T, R, B; }
    delegate bool EnumProc(IntPtr h, IntPtr p);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out Rect r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr h);
    [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(nint c);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out Rect r, int size);
}
