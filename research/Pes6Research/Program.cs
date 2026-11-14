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
