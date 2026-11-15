namespace Pes6Research;

/// <summary>Where the game keeps the match state (see research/NOTES.md). Shared by the commands that read players and ball.</summary>
static class GameLayout
{
    /// <summary>Byte: id (1-22) of the player who has the ball, 0 or more than 22 when nobody has it.</summary>
    public const uint Carrier = 0x3BE09CC;

    /// <summary>Pointer to the ball; its x, y, z floats are at +0x10. Null before a match.</summary>
    public const uint BallPointer = 0xBCCE94;

    /// <summary>The 22 players, 0x240 bytes each: ids 1-11 and 12-22 are the two teams, slot 0 is the keeper.</summary>
    public const uint Players = 0x3BDC980 + PlayerSize;
    public const int PlayerSize = 0x240, PlayerCount = 22;

    // Offsets in a player. Angles are 16-bit (0x10000 = 360 degrees).
    public const int AnimationPointer = 0x04, Role = 0x10, Slot = 0x11, Team = 0x12, State = 0x16, Timer = 0x18, Speed = 0x1C,
        Facing = 0x26, Move = 0x28, Heading = 0x2C, Asked = 0x2E, TargetX = 0x6C, TargetZ = 0x70, Buttons = 0xB0, X = 0xE0, Z = 0xE8,
        PlacementMode = 0x1DD;   // the positioning dispatcher 0x528580 switches on it (one placement routine per mode)

    // Offsets in the animation a player's +0x04 points to.
    public const int AnimationId = 0x30, AnimationFrame = 0x34;

    // Offsets in the ball (from the pointer): position floats at +0x10, speed int32 at +0x50, direction (angle) at +0x58.
    public const int BallPosition = 0x10, BallSpeed = 0x50, BallDirection = 0x58;

    /// <summary>Controller 1: stick angle (16-bit), then its magnitude (byte, 0 = at rest).</summary>
    public const uint Pad = 0x1016680;

    /// <summary>A 16-bit game angle in degrees, rounded as the scripts did (to even).</summary>
    public static double Degrees(ushort angle, int decimals = 0) => Math.Round(angle * 360.0 / 65536, decimals);

    /// <summary>Address of a player by id (1-22).</summary>
    public static uint Player(int id) => Players + (uint)((id - 1) * PlayerSize);

    /// <summary>The ball pointer, or 0 (before a match, or unreadable).</summary>
    public static uint BallAddress(IGameMemory game) => BitConverter.ToUInt32(game.ReadOrZero(BallPointer, 4));

    /// <summary>The ball's x, y, z; zeros when it cannot be read (before a match).</summary>
    public static (float X, float Y, float Z) Ball(IGameMemory game)
    {
        var pointer = new byte[4];
        var ball = new byte[12];
        var bp = game.TryRead(BallPointer, pointer) ? BitConverter.ToUInt32(pointer) : 0;
        if (bp == 0 || !game.TryRead(bp + 0x10, ball)) return (0, 0, 0);
        return (BitConverter.ToSingle(ball, 0), BitConverter.ToSingle(ball, 4), BitConverter.ToSingle(ball, 8));
    }
}
