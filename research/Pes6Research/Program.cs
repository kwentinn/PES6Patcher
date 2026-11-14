using Pes6Research;

// pes6r <command> [options]. Every command takes -Process <name> (PES6 by default).
var commands = new Dictionary<string, (Func<Options, string, int> Run, string[] Switches, string Usage)>(StringComparer.OrdinalIgnoreCase)
{
    // the running game's memory
    ["dump"] = (DumpCommand.Run, [], DumpCommand.Usage),
    ["code"] = (CodeCommand.Run, [], CodeCommand.Usage),
    ["patch"] = (PatchCommand.Run, PatchCommand.Switches, PatchCommand.Usage),
    ["players"] = (PlayersCommand.Run, [], PlayersCommand.Usage),
    ["positions"] = (PositionsCommand.Run, [], PositionsCommand.Usage),
    ["shape"] = (ShapeCommand.Run, [], ShapeCommand.Usage),
    // guided by beeps while someone plays
    ["driftlog"] = (CarrierLogCommands.Drift, [], CarrierLogCommands.DriftUsage),
    ["turnlog"] = (CarrierLogCommands.Turn, [], CarrierLogCommands.TurnUsage),
    ["heading"] = (HeadingCommand.Run, [], HeadingCommand.Usage),
    ["stick"] = (StickCommand.Run, [], StickCommand.Usage),
    // attach the debugger (hardware breakpoints): keep the sessions short
    ["calls"] = (BreakpointCommands.Calls, [], BreakpointCommands.CallsUsage),
    ["hits"] = (BreakpointCommands.Hits, [], BreakpointCommands.HitsUsage),
    ["watch"] = (BreakpointCommands.Watch, ["Read"], BreakpointCommands.WatchUsage),
    ["viewport"] = (BreakpointCommands.Viewport, [], BreakpointCommands.ViewportUsage),
    // unattended runs in a CPU-vs-CPU match
    ["ab"] = (AbCommand.Run, [], AbCommand.Usage),
    ["scoreprobe"] = (ScoreProbeCommand.Run, [], ScoreProbeCommand.Usage),
    // windows
    ["window"] = (WindowCommands.Window, [], WindowCommands.WindowUsage),
    ["capture"] = (WindowCommands.Capture, [], WindowCommands.CaptureUsage),
    // files (the game need not run)
    ["depth"] = (DepthCommand.Run, [], DepthCommand.Usage),
    ["findref"] = (ExeCommands.FindRef, [], ExeCommands.FindRefUsage),
    ["readva"] = (ExeCommands.ReadVa, [], ExeCommands.ReadVaUsage),
};

if (args.Length == 0 || !commands.TryGetValue(args[0], out var command))
{
    if (args.Length > 0 && args[0] is not ("help" or "-h" or "--help" or "-?")) Console.Error.WriteLine($"unknown command '{args[0]}'");
    Console.Error.WriteLine("pes6r <command> [options]   (-Process <name>: another process than PES6)");
    foreach (var c in commands.Values) Console.Error.WriteLine("  " + c.Usage.ReplaceLineEndings(Environment.NewLine + "  "));
    return args.Length == 0 ? 0 : 2;
}

try
{
    var options = new Options(args.Skip(1), command.Switches);
    var process = options.String("Process") ?? "PES6";
    return command.Run(options, process);
}
catch (ToolException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(e.Message); // a file that cannot be written (open elsewhere) or read
    return 1;
}
