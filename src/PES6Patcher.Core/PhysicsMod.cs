namespace PES6Patcher.Core;

/// <summary>
/// PityCharly's physics mod (pass power, ball weight): a Kitserver DLL distributed by its author.
/// The user copies the DLL into the kitserver folder; the patcher only adds or removes its line in
/// kload.cfg. Its licence asks for <see cref="Credit"/> to be shown visibly.
/// </summary>
public static class PhysicsMod
{
    public const string FileName = "pes6_game_physics_mod.dll";
    public const string KloadPath = @"kitserver\" + FileName;
    public const string Url = "https://github.com/angelballay/pes6_game_physics_mod";
    public const string Credit = "Mod de Física Original creado por PityCharly.";
}
