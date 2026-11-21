using System.Runtime.InteropServices;

namespace Pes6Research;

/// <summary>
/// Presses a key in the game to leave a goal sequence (celebration, replay), which waits for Start. The game reads the keyboard
/// with DirectInput, which ignores window messages, so the key is sent as a scan code to the foreground window: the game is brought
/// to the front first (Alt is tapped so Windows lets it take the focus). The key is NOT sent when the game did not come to the
/// front, so it never lands in another window.
/// </summary>
static class SkipKey
{
    /// <summary>True when the key was sent; false (and nothing sent) when the game has no window or could not take the focus.</summary>
    public static bool Press(GameProcess game, int scanCode, int holdMs = 90)
    {
        var window = game.MainWindow;
        if (window == IntPtr.Zero) return false;
        keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, KeyUp, UIntPtr.Zero);
        SetForegroundWindow(window);
        Thread.Sleep(150);
        if (GetForegroundWindow() != window) return false;
        // a scan code over 0xFF is an extended key (the arrows: 328 up, 336 down, 331 left, 333 right); without the flag
        // DirectInput would see the numeric pad
        var flags = ScanCode | (scanCode > 0xFF ? Extended : 0);
        keybd_event(0, (byte)scanCode, flags, UIntPtr.Zero); Thread.Sleep(holdMs);
        keybd_event(0, (byte)scanCode, flags | KeyUp, UIntPtr.Zero);
        return true;
    }

    const uint Extended = 0x1, KeyUp = 0x2, ScanCode = 0x8;

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
