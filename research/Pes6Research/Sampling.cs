using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pes6Research;

/// <summary>
/// What the sampling commands share: beeps to guide the person at the controller, Sleep to the millisecond (not to the
/// 15.6 ms tick), and Ctrl+C that ends the run cleanly (the file is still written) instead of killing it.
/// </summary>
sealed class Sampling : IDisposable
{
    volatile bool stopped;
    public Stopwatch Clock { get; } = new();

    public Sampling()
    {
        timeBeginPeriod(1);
        Console.CancelKeyPress += OnCancel;
    }

    /// <summary>Waits the start delay, beeps high, and starts the clock.</summary>
    public void Start(int startDelaySeconds, bool beep = true)
    {
        if (startDelaySeconds > 0) Wait(startDelaySeconds * 1000);
        if (beep) Beep(1200, 300);
        Clock.Restart();
    }

    /// <summary>Beeps low: the end of the run.</summary>
    public static void End() => Beep(600, 700);

    /// <summary>True while the time is not up, Ctrl+C was not pressed and the game still runs.</summary>
    public bool Running(GameProcess game, double seconds)
    {
        if (stopped || Clock.Elapsed.TotalSeconds >= seconds) return false;
        if (game.HasExited) { Console.Error.WriteLine("the game closed"); return false; }
        return true;
    }

    /// <summary>Sleeps what is left of <paramref name="every"/> ms since <paramref name="startMs"/> on the clock.</summary>
    public void Pace(int every, long startMs)
    {
        var left = every - (Clock.ElapsedMilliseconds - startMs);
        if (left > 0) Thread.Sleep((int)left);
    }

    void Wait(int ms)
    {
        var end = Environment.TickCount64 + ms;
        while (!stopped && Environment.TickCount64 < end) Thread.Sleep(20);
    }

    void OnCancel(object? sender, ConsoleCancelEventArgs e) { e.Cancel = true; stopped = true; }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancel;
        timeEndPeriod(1);
    }

    [DllImport("kernel32.dll")] public static extern bool Beep(int frequency, int ms);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);
}
