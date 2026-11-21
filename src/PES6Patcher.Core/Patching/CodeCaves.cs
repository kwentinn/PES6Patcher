namespace PES6Patcher.Core.Patching;

/// <summary>A hook (<see cref="NameKey"/> = resource key of its name): original bytes at <see cref="Entry"/> replaced by a jump to code placed in a cave.</summary>
internal sealed record Hook(string NameKey, int Entry, byte[] EntryBytes, int Cave, byte[] CaveBytes);

/// <summary>
/// Code added to PES6.exe. Every address comes from <see cref="ExeLocator"/>, so the same code works
/// in builds where the game code lives elsewhere. See research/NOTES.md for how each fix was found.
/// </summary>
internal static class CodeCaves
{
	public const int ClipSize = 60, CursorSize = 19, FreeDirectionSize = 156, ForwardRunsSize = 15;

	/// <summary>Kick relay cave: dribble direction part, close dribbling part, replayed bytes and jump back.</summary>
	public const int RelayDirectionSize = 79, RelayCloseDribbleSize = 184, RelayStrengthKeySize = 13, RelayTailSize = 11;

	/// <summary>Key that switches close dribbling on and off in game.</summary>
	public const byte CloseDribbleSwitchKey = 0x70;   // VK_F1

	public static int KickRelaySize(bool direction, bool closeDribble, bool strengthKey = false) =>
		(direction ? RelayDirectionSize : 0) + (closeDribble ? RelayCloseDribbleSize + (strengthKey ? RelayStrengthKeySize : 0) : 0) + RelayTailSize;

	/// <summary>Close dribbling: touch speed x 14/16 while running, x 13/16 while sprinting (tuned in game).</summary>
	public const byte RunTouchSixteenths = 14, SprintTouchSixteenths = 13;

	/// <summary>
	/// The close dribbling reduction follows the player's dribble accuracy: none up to <see cref="MinAccuracy"/>
	/// (the game's own touches), full from <see cref="MaxAccuracy"/>, in proportion between.
	/// </summary>
	public const byte MinAccuracy = 65, MaxAccuracy = 95;

	/// <summary>Acceleration gap: the shift count of "sar edx,2" (/10 with the multiply before it) becomes 1 (/5).</summary>
	public const byte AccelerationGapShift = 1;

	/// <summary>F10 changes the close dribbling strength by 25 %.</summary>
	public const byte CloseDribbleStrengthKey = 0x79;   // VK_F10
	public const int CloseDribbleStrengthStep = 25;

	public static TestKey CloseDribbleStrengthTestKey(int percent, int texts, int stride) =>
		new(CloseDribbleStrengthKey, CloseDribbleStrengthState, percent, PatchOptions.MinCloseDribbleStrength, PatchOptions.MaxCloseDribbleStrength,
			texts, stride, CloseDribbleStrengthStep);

	/// <summary>
	/// clip(x0, y0, x1, y1) converts the 2D clip rectangle to pixels with x * screenWidth / 512.
	/// The cave rescales x0 and x1 by 512 / (2 * half), then replays the 10 replaced bytes
	/// (push ecx/ebx/ebp/esi/edi ; call screenWidth).
	/// </summary>
	public static Hook Clip(int half, int cave, int entry, int screenWidthGetter)
	{
		var code = new CodeBuilder(cave)
			.Bytes(0x50, 0x52, 0x51)                            // push eax ; push edx ; push ecx
			.Bytes(0x8B, 0x44, 0x24, 0x10)                      // mov eax,[esp+0x10]   ; x0
			.Bytes(0x69, 0xC0, 0x00, 0x02, 0x00, 0x00)          // imul eax,eax,512
			.Bytes(0x99)                                        // cdq
			.Bytes([0xB9], 2 * half)                            // mov ecx,2*half
			.Bytes(0xF7, 0xF9)                                  // idiv ecx
			.Bytes(0x89, 0x44, 0x24, 0x10)                      // mov [esp+0x10],eax
			.Bytes(0x8B, 0x44, 0x24, 0x18)                      // mov eax,[esp+0x18]   ; x1
			.Bytes(0x69, 0xC0, 0x00, 0x02, 0x00, 0x00)          // imul eax,eax,512
			.Bytes(0x99)                                        // cdq
			.Bytes(0xF7, 0xF9)                                  // idiv ecx
			.Bytes(0x89, 0x44, 0x24, 0x18)                      // mov [esp+0x18],eax
			.Bytes(0x59, 0x5A, 0x58)                            // pop ecx ; pop edx ; pop eax
			.Bytes(0x51, 0x53, 0x55, 0x56, 0x57)                // original: push ecx/ebx/ebp/esi/edi
			.CallTo(screenWidthGetter)                          // original: call screenWidth
			.Build();
		code = [.. code, .. CodeBuilder.JumpPatch(cave + code.Length, entry + 10, 5)];
		return new Hook("Hook.Clip", entry, CodeBuilder.JumpPatch(entry, cave, 10), cave, code);
	}

	/// <summary>
	/// The 3D -> 2D layer projection gets an x scale of viewportWidth (512) / aspect from
	/// "fild [esp+0x4C] ; fmul [esp+0x40]". The cave replays them, then multiplies by
	/// (2 * half) / 512, stored at <paramref name="scaleAddress"/>.
	/// </summary>
	public static Hook Cursor(int cave, int entry, int scaleAddress)
	{
		var code = new CodeBuilder(cave)
			.Bytes(0xDB, 0x44, 0x24, 0x4C, 0xD8, 0x4C, 0x24, 0x40)   // original: fild [esp+0x4C] ; fmul [esp+0x40]
			.Bytes([0xD8, 0x0D], scaleAddress)                       // fmul dword [scale]
			.Build();
		code = [.. code, .. CodeBuilder.JumpPatch(cave + code.Length, entry + 8, 5)];
		return new Hook("Hook.Cursor", entry, CodeBuilder.JumpPatch(entry, cave, 8), cave, code);
	}

	/// <summary>
	/// Forwards as runners: the runner picker leaves at once for roles 24 and over, so a forward never made the run beyond the
	/// last line (mode 0x0F, one runner a side). The jump on those roles goes to a cave that takes a candidate of role 24 to 64
	/// as the runner (the code that stores his id in the side's slot), and leaves the other roles to the jump's old target.
	/// </summary>
	public static Hook ForwardRuns(int cave, ForwardRunsSites sites)
	{
		var code = new CodeBuilder(cave)
			.Bytes(0x83, 0xF8, 0x40)        // cmp eax,0x40      ; role - 1 (the goalkeeper's 0 wraps to a huge value: not taken)
			.Jump(0x73, "skip")             // jae skip
			.JumpTo(sites.Accept)           // jmp accept        ; the candidate runs
			.Mark("skip")
			.JumpTo(sites.Skip)             // jmp the old target
			.Build();
		return new Hook("Hook.ForwardRuns", sites.Test, CodeBuilder.JumpPatch(sites.Test, cave, 6), cave, code);
	}

	/// <summary>The ball push's state (<see cref="BallPushSites.State"/>): byte +0 R1 was down, dword +4 frame, +8 frame of the last R1 tap, +0xC taps, +0x10 armed (1 = R1 x3, 2 = right stick), +0x14 frame armed, byte +0x18 stick was down, dword +0x1C stick angle.</summary>
	private const int PushR1Down = 0, PushFrame = 4, PushLastTap = 8, PushTaps = 0xC, PushArmed = 0x10, PushArmedFrame = 0x14, PushStickDown = 0x18, PushStickAngle = 0x1C;

	/// <summary>Frames (60 a second): three R1 taps within 36 of each other arm the push, which lasts 90; the right stick counts as pushed from 7 of 8.</summary>
	private const byte PushTapWindow = 36, PushArmedFrames = 90, PushStickAmplitude = 7;

	/// <summary>Right stick push: the touch goes at most 20 degrees (0x0E39 of 0x10000) from the direction the player runs.</summary>
	private const int PushMaxTurn = 0x0E39;

	/// <summary>
	/// Ball push, pad half: at the entry of the pad reader (it replays the 7 bytes it replaces), for pad 0 only, once a frame, it counts the
	/// taps of R1 (button word 0x0800, 0 = pressed) and watches the right stick (the game's own table: amplitude 7 or more): three taps
	/// within <see cref="PushTapWindow"/> frames arm the push (1), the stick pushed to the edge arms it too (2, with the stick's angle);
	/// the push is dropped after <see cref="PushArmedFrames"/> frames. <see cref="BallPushTouch"/> uses it.
	/// </summary>
	public static Hook BallPushPad(int cave, BallPushSites sites)
	{
		int s = sites.State;
		var code = new CodeBuilder(cave)
			.Bytes(0x51, 0x53, 0x55, 0x8B, 0x6C, 0x24, 0x10)       // push ecx ; push ebx ; push ebp ; mov ebp,[esp+0x10]   ; the replaced bytes
			.Bytes(0x50, 0x52)                                      // push eax ; push edx
			.Bytes(0x85, 0xED).JumpNear(0x85, "done")               // test ebp,ebp ; jnz done   ; pad 0 only
			.Bytes([0xFF, 0x05], s + PushFrame)                     // inc [frame]
			.Bytes([0x0F, 0xB7, 0x05], sites.PadBase)               // movzx eax,word [buttons]
			.Bytes(0x25, 0x00, 0x08, 0x00, 0x00)                    // and eax,0x800
			.Bytes(0x0F, 0x94, 0xC2)                                // sete dl           ; 1 = R1 pressed
			.Bytes([0x0F, 0xB6, 0x05], s + PushR1Down)              // movzx eax,byte [R1 was down]
			.Bytes([0x88, 0x15], s + PushR1Down)                    // mov [R1 was down],dl
			.Bytes(0x84, 0xC0).Jump(0x75, "tapsDone")               // test al,al ; jnz tapsDone   ; held, not a new tap
			.Bytes(0x84, 0xD2).Jump(0x74, "tapsDone")               // test dl,dl ; jz tapsDone
			.Bytes([0xA1], s + PushFrame)                           // mov eax,[frame]
			.Bytes([0x8B, 0x15], s + PushLastTap)                   // mov edx,[last tap]
			.Bytes([0xA3], s + PushLastTap)                         // mov [last tap],eax
			.Bytes(0x2B, 0xC2)                                      // sub eax,edx
			.Bytes(0x83, 0xF8, PushTapWindow).Jump(0x76, "keep")    // cmp eax,36 ; jbe keep
			.Bytes([0xC7, 0x05], s + PushTaps).Bytes(0, 0, 0, 0)    // mov [taps],0      ; too late: start again
			.Mark("keep")
			.Bytes([0xFF, 0x05], s + PushTaps)                      // inc [taps]
			.Bytes([0x83, 0x3D], s + PushTaps).Bytes(3)             // cmp [taps],3
			.Jump(0x72, "tapsDone")                                 // jb tapsDone
			.Bytes([0xC7, 0x05], s + PushTaps).Bytes(0, 0, 0, 0)    // mov [taps],0
			.Bytes([0xC7, 0x05], s + PushArmed).Bytes(1, 0, 0, 0)   // mov [armed],1
			.Bytes([0xA1], s + PushFrame)                           // mov eax,[frame]
			.Bytes([0xA3], s + PushArmedFrame)                      // mov [armed frame],eax
			.Mark("tapsDone")
			.Bytes([0x0F, 0xB6, 0x05], sites.RightStick + 2)        // movzx eax,byte [right stick amplitude]
			.Bytes(0x83, 0xF8, PushStickAmplitude)                  // cmp eax,7
			.Bytes(0x0F, 0x93, 0xC2)                                // setae dl          ; 1 = pushed to the edge
			.Bytes([0x0F, 0xB6, 0x05], s + PushStickDown)           // movzx eax,byte [stick was down]
			.Bytes([0x88, 0x15], s + PushStickDown)                 // mov [stick was down],dl
			.Bytes(0x84, 0xC0).Jump(0x75, "stickDone")              // test al,al ; jnz stickDone
			.Bytes(0x84, 0xD2).Jump(0x74, "stickDone")              // test dl,dl ; jz stickDone
			.Bytes([0x0F, 0xB7, 0x05], sites.RightStick)            // movzx eax,word [right stick angle]
			.Bytes([0xA3], s + PushStickAngle)                      // mov [stick angle],eax
			.Bytes([0xC7, 0x05], s + PushArmed).Bytes(2, 0, 0, 0)   // mov [armed],2
			.Bytes([0xA1], s + PushFrame)                           // mov eax,[frame]
			.Bytes([0xA3], s + PushArmedFrame)                      // mov [armed frame],eax
			.Mark("stickDone")
			.Bytes([0x83, 0x3D], s + PushArmed).Bytes(0)            // cmp [armed],0
			.Jump(0x74, "done")                                     // je done
			.Bytes([0xA1], s + PushFrame)                           // mov eax,[frame]
			.Bytes([0x2B, 0x05], s + PushArmedFrame)                // sub eax,[armed frame]
			.Bytes(0x83, 0xF8, PushArmedFrames).Jump(0x76, "done") // cmp eax,90 ; jbe done
			.Bytes([0xC7, 0x05], s + PushArmed).Bytes(0, 0, 0, 0)   // mov [armed],0     ; too old
			.Mark("done")
			.Bytes(0x5A, 0x58)                                      // pop edx ; pop eax
			.JumpTo(sites.PadEntry + 7)                             // back after the replaced bytes
			.Build();
		return new Hook("Hook.BallPush", sites.PadEntry, CodeBuilder.JumpPatch(sites.PadEntry, cave, 7), cave, code);
	}

	/// <summary>
	/// Ball push, touch half: the dribble relay's call of the kick function (touch: player, direction, speed, height, flag) goes through
	/// this cave. When the push is armed and the touch is a human team's (the score learnt at the kick function's entry), the speed is
	/// raised by a quarter (R1 x3) or a half (right stick, only while the player runs: state 2, or 25 after R1); with the stick, the
	/// direction becomes the stick's, at most 20 degrees from the direction the player runs (his left stick if pushed, else his
	/// orientation +0x26), and the player himself is left alone. One touch only. The kick function is then jumped to, so it returns to the relay.
	/// </summary>
	public static Hook BallPushTouch(int cave, BallPushSites sites, ShotSites humans)
	{
		int s = sites.State;
		var code = new CodeBuilder(cave)
			.Bytes([0x83, 0x3D], s + PushArmed).Bytes(0)            // cmp [armed],0
			.JumpNear(0x84, "go")                                   // je go
			.Bytes(0x8B, 0x44, 0x24, 0x04)                          // mov eax,[esp+4]   ; player
			.Bytes(0x0F, 0xB6, 0x48, 0x12, 0x83, 0xE1, 0x01)        // movzx ecx,byte [eax+0x12] ; and ecx,1 ; team
			.Bytes([0x80, 0xB9, .. BitConverter.GetBytes(humans.HumanScores), HumanScore])   // cmp byte [ecx+scores],HumanScore
			.JumpNear(0x82, "go")                                   // jb go             ; not a human team
			.Bytes(0xB1, 0x02)                                      // mov cl,2          ; speed + speed / 4
			.Bytes([0x83, 0x3D], s + PushArmed).Bytes(2)            // cmp [armed],2
			.Jump(0x75, "speed")                                    // jne speed
			.Bytes(0x0F, 0xB6, 0x50, 0x16)                          // movzx edx,byte [eax+0x16]   ; state
			.Bytes(0x83, 0xFA, 0x02).Jump(0x74, "running")          // cmp edx,2 ; je running      ; run or sprint
			.Bytes(0x83, 0xFA, 0x19).JumpNear(0x85, "go")           // cmp edx,25 ; jne go         ; the touch after R1
			.Mark("running")
			.Bytes(0x0F, 0xB7, 0x48, 0x26)                          // movzx ecx,word [eax+0x26]   ; orientation
			.Bytes([0x80, 0x3D], sites.LeftStick + 2).Bytes(0)      // cmp byte [left stick amplitude],0
			.Jump(0x74, "haveRef")                                  // je haveRef
			.Bytes([0x0F, 0xB7, 0x0D], sites.LeftStick)             // movzx ecx,word [left stick angle]
			.Mark("haveRef")
			.Bytes([0x8B, 0x15], s + PushStickAngle)                // mov edx,[stick angle]
			.Bytes(0x2B, 0xD1)                                      // sub edx,ecx
			.Bytes(0x0F, 0xBF, 0xD2)                                // movsx edx,dx      ; the turn from the running direction
			.Bytes([0x81, 0xFA], PushMaxTurn).Jump(0x7E, "low")     // cmp edx,max ; jle low
			.Bytes([0xBA], PushMaxTurn)                             // mov edx,max
			.Mark("low")
			.Bytes([0x81, 0xFA], -PushMaxTurn).Jump(0x7D, "high")   // cmp edx,-max ; jge high
			.Bytes([0xBA], -PushMaxTurn)                            // mov edx,-max
			.Mark("high")
			.Bytes(0x03, 0xD1)                                      // add edx,ecx
			.Bytes(0x0F, 0xB7, 0xD2)                                // movzx edx,dx
			.Bytes(0x89, 0x54, 0x24, 0x08)                          // mov [esp+8],edx   ; the touch's direction
			.Bytes(0xB1, 0x01)                                      // mov cl,1          ; speed + speed / 2
			.Mark("speed")
			.Bytes(0x8B, 0x44, 0x24, 0x0C)                          // mov eax,[esp+0xC] ; speed
			.Bytes(0x8B, 0xD0, 0xD3, 0xFA, 0x03, 0xC2)              // mov edx,eax ; sar edx,cl ; add eax,edx
			.Bytes(0x89, 0x44, 0x24, 0x0C)                          // mov [esp+0xC],eax
			.Bytes([0xC7, 0x05], s + PushArmed).Bytes(0, 0, 0, 0)   // mov [armed],0     ; one touch
			.Mark("go")
			.JumpTo(sites.KickEntry)                                // the kick function returns to the relay
			.Build();
		return new Hook("Hook.BallPush", sites.RelayCall, CodeBuilder.CallPatch(sites.RelayCall, cave, 5), cave, code);
	}

	/// <summary>
	/// The direction function turns the 8-way direction bits into a heading (its body is virtualised
	/// by the exe protection). The cave calls its original body (copied as is), then - only when the
	/// bits are the current human input and match a pad's analog stick - adds the offset between that
	/// stick's precise angle (stickTable + pad * 4) and the centre of its 45-degree sector.
	/// </summary>
	public static Hook FreeDirection(int cave, DirectionSites sites)
	{
		var code = new CodeBuilder(cave)
			.Bytes(0x8B, 0x44, 0x24, 0x04)                      // mov eax,[esp+4]       ; bits argument
			.Bytes(0x50)                                        // push eax
			.CallTo("stub")                                     // call original body
			.Bytes(0x83, 0xC4, 0x04)                            // add esp,4
			.Bytes(0x53, 0x51, 0x52, 0x56, 0x57)                // push ebx/ecx/edx/esi/edi
			.Bytes(0x8B, 0xD8)                                  // mov ebx,eax           ; original heading
			.Bytes(0x0F, 0xB6, 0x4C, 0x24, 0x18)                // movzx ecx,byte [esp+0x18]
			.Bytes([0x81, 0xE1], 0xF0)                          // and ecx,0xF0
			.Jump(0x74, "done")                                 // jz done
			.Bytes([0x0F, 0xB6, 0x15], sites.DirectionBits)     // movzx edx,byte [human input bits]
			.Bytes([0x81, 0xE2], 0xF0)                          // and edx,0xF0
			.Bytes(0x3B, 0xCA)                                  // cmp ecx,edx
			.Jump(0x75, "done")                                 // jne done
			.Bytes(0x33, 0xF6)                                  // xor esi,esi           ; pad index
			.Mark("loop")
			.Bytes([.. new byte[] { 0x80, 0x3C, 0xB5 }, .. BitConverter.GetBytes(sites.StickTable + 2), 0x00])   // cmp byte [esi*4+magnitude],0
			.Jump(0x74, "next")                                 // je next
			.Bytes([0x0F, 0xB6, 0x14, 0xB5], sites.StickTable + 3)   // movzx edx,byte [esi*4+bits]
			.Bytes(0x51, 0x52)                                  // push ecx ; push edx
			.CallTo(sites.RotateBits)                           // call rotate (attacking side)
			.Bytes(0x83, 0xC4, 0x04, 0x59)                      // add esp,4 ; pop ecx
			.Bytes(0x0F, 0xB6, 0xC0)                            // movzx eax,al
			.Bytes([0x25], 0xF0)                                // and eax,0xF0
			.Bytes(0x3B, 0xC1)                                  // cmp eax,ecx
			.Jump(0x74, "found")                                // je found
			.Mark("next")
			.Bytes(0x46, 0x83, 0xFE, 0x08)                      // inc esi ; cmp esi,8
			.Jump(0x7C, "loop")                                 // jl loop
			.Jump(0xEB, "done")                                 // jmp done
			.Mark("found")
			.Bytes([0x0F, 0xB7, 0x04, 0xB5], sites.StickTable)  // movzx eax,word [esi*4+angle]
			.Bytes(0x8B, 0xD0)                                  // mov edx,eax
			.Bytes([0x81, 0xC2], 0x1000)                        // add edx,0x1000
			.Bytes([0x81, 0xE2], 0xE000)                        // and edx,0xE000
			.Bytes(0x2B, 0xC2)                                  // sub eax,edx           ; offset in sector
			.Bytes(0x90, 0x90)                                  // (room for "neg eax" if conventions differ)
			.Bytes(0x03, 0xD8)                                  // add ebx,eax
			.Mark("done")
			.Bytes(0x8B, 0xC3)                                  // mov eax,ebx
			.Bytes(0x5F, 0x5E, 0x5A, 0x59, 0x5B)                // pop edi/esi/edx/ecx/ebx
			.Bytes(0xC3)                                        // ret
			.Mark("stub")
			.Bytes(sites.OriginalBody)                          // original body, ends with jmp [virtualised body]
			.Build();
		return new Hook("Hook.FreeDirection", sites.Entry, CodeBuilder.JumpPatch(sites.Entry, cave, 7), cave, code);
	}

	/// <summary>
	/// relay(player, direction, speed, height) kicks the ball. While dribbling, the player runs after the
	/// ball and can only change direction at the next touch. The cave, depending on the options:
	/// <list type="bullet">
	/// <item>direction (with the free direction): the most frequent dribble touch takes the stick angle
	/// rounded to 22.5 degrees (by virtualised code). For the pad whose rounded angle is that direction
	/// (or its opposite: attacking side), the cave adds back what the rounding removed. The AI's touches
	/// match no pad, so they are left alone.</item>
	/// <item>close dribbling: for the dribble touches, scales the speed argument down (shorter touches), by
	/// <paramref name="strength"/> % of the reduction (or the F10 value read at <paramref name="strengthState"/>, when not 0).</item>
	/// </list>
	/// Then it replays the 6 replaced bytes (mov ecx,[esp+0xC] ; mov eax,ecx). Passes and shots are left alone.
	/// </summary>
	public static Hook KickRelay(int cave, DribbleSites sites, DirectionSites? direction, bool closeDribble, int switchSetUp = 0,
		int strength = 100, int strengthState = 0)
	{
		int state = sites.SwitchState;
		byte[] StateByte(byte op, int offset, byte value) =>   // op byte [state+offset],value
			[0x80, op, .. BitConverter.GetBytes(state + offset), value];
		var code = new CodeBuilder(cave);
		if (direction != null)
		{
			int table = direction.StickTable;
			code.Bytes(0x8B, 0x04, 0x24)                            // mov eax,[esp]        ; return address
				.Bytes([0x3D], sites.SnappedTouch)                  // cmp eax,snapped touch
				.Jump(0x75, "directionDone")                        // jne directionDone
				.Bytes(0x51, 0x52, 0x56)                            // push ecx/edx/esi
				.Bytes(0x33, 0xF6)                                  // xor esi,esi          ; pad index
				.Mark("pad")
				.Bytes([.. new byte[] { 0x80, 0x3C, 0xB5 }, .. BitConverter.GetBytes(table + 2), 0x00])   // cmp byte [esi*4+magnitude],0
				.Jump(0x74, "nextPad")                              // je nextPad
				.Bytes([0x0F, 0xB7, 0x0C, 0xB5], table)             // movzx ecx,word [esi*4+angle]
				.Bytes(0x8B, 0xD1)                                  // mov edx,ecx
				.Bytes([0x81, 0xC2], 0x800)                         // add edx,0x800
				.Bytes([0x81, 0xE2], 0xF000)                        // and edx,0xF000       ; rounded to 22.5 deg
				.Bytes(0x8B, 0x44, 0x24, 0x14)                      // mov eax,[esp+0x14]   ; direction argument
				.Bytes(0x2B, 0xC2)                                  // sub eax,edx
				.Bytes(0x66, 0xA9, 0xFF, 0x7F)                      // test ax,0x7FFF       ; same or opposite?
				.Jump(0x75, "nextPad")                              // jne nextPad
				.Bytes(0x2B, 0xCA)                                  // sub ecx,edx          ; removed by the rounding
				.Bytes(0x0F, 0xBF, 0xC9)                            // movsx ecx,cx
				.Bytes(0x01, 0x4C, 0x24, 0x14)                      // add [esp+0x14],ecx
				.Jump(0xEB, "restore")                              // jmp restore
				.Mark("nextPad")
				.Bytes(0x46, 0x83, 0xFE, 0x08)                      // inc esi ; cmp esi,8
				.Jump(0x7C, "pad")                                  // jl pad
				.Mark("restore")
				.Bytes(0x5E, 0x5A, 0x59)                            // pop esi/edx/ecx
				.Mark("directionDone");
		}
		if (closeDribble)
		{
			// first touch: set up the Kitserver message (F1 then read on every frame); without it,
			// F1 pressed since the last touch switches here (eax, ecx, edx are free at the relay's entry)
			code.Bytes(StateByte(0x3D, 1, 0))                      // cmp byte [set up],0
				.Jump(0x75, "setUp")                                // jne setUp
				.CallTo(switchSetUp)                                // call switch set-up
				.Mark("setUp")
				.Bytes(StateByte(0x3D, 2, 0))                      // cmp byte [message hook],0
				.Jump(0x75, "switched")                             // jne switched          ; read on every frame
				.Bytes(0x6A, CloseDribbleSwitchKey)                 // push VK_F1
				.Bytes([0xFF, 0x15], sites.KeyStateImport)          // call [GetAsyncKeyState]
				.Bytes(0xA8, 0x01)                                  // test al,1             ; pressed since last call
				.Jump(0x74, "switched")                             // je switched
				.Bytes(StateByte(0x35, 0, 1))                      // xor byte [off],1
				.Mark("switched")
				.Bytes(StateByte(0x3D, 0, 0))                      // cmp byte [off],0
				.Jump(0x75, "original")                             // jne original          ; switched off
				.Bytes(0x8B, 0x04, 0x24);                           // mov eax,[esp]        ; return address
			foreach (int touch in sites.RunTouches)
				code.Bytes([0x3D], touch).Jump(0x74, "run");        // cmp eax,touch ; je run
			foreach (int touch in sites.SprintTouches)
				code.Bytes([0x3D], touch).Jump(0x74, "sprint");     // cmp eax,touch ; je sprint
																	// the scaling lives after the original bytes, within short jump range of everything
			Original(code)
				// speed -= speed * (16 - sixteenths) / 16 * (accuracy - min) / (max - min) * strength / 100, accuracy capped at max
				.Mark("run")
				.Bytes(0x6A, 16 - RunTouchSixteenths, 0x59)         // push ; pop ecx      ; full reduction
				.Jump(0xEB, "scale")                                // jmp scale
				.Mark("sprint")
				.Bytes(0x6A, 16 - SprintTouchSixteenths, 0x59)      // push ; pop ecx
				.Mark("scale")
				.Bytes(0x8B, 0x44, 0x24, 0x04)                      // mov eax,[esp+4]      ; player
				.Bytes(0x0F, 0xB6, 0x50, 0x12)                      // movzx edx,byte [eax+0x12]
				.Bytes(0x0F, 0xB6, 0x40, 0x11)                      // movzx eax,byte [eax+0x11]
				.Bytes(0xC1, 0xE2, 0x05, 0x03, 0xC2)                // shl edx,5 ; add eax,edx
				.Bytes([0x69, 0xC0], 0x348)                         // imul eax,eax,0x348
				.Bytes([0x8B, 0x80], sites.DribbleAccuracy)         // mov eax,[eax+dribble accuracy]
				.Bytes(0x83, 0xE0, 0x7F)                            // and eax,0x7F
				.Bytes(0x83, 0xE8, MinAccuracy)                     // sub eax,min
				.Jump(0x7E, "original")                             // jle original         ; weak dribbler: game's touch
				.Bytes(0x83, 0xF8, MaxAccuracy - MinAccuracy)       // cmp eax,max-min
				.Jump(0x7E, "capped")                               // jle capped
				.Bytes([0xB8], MaxAccuracy - MinAccuracy)           // mov eax,max-min
				.Mark("capped")
				.Bytes(0x0F, 0xAF, 0xC1);                           // imul eax,ecx
			if (strengthState != 0)
				code.Bytes([0x0F, 0xB7, 0x0D], strengthState)       // movzx ecx,word [strength]
					.Bytes(0x85, 0xC9)                              // test ecx,ecx
					.Jump(0x75, "strengthSet")                      // jne strengthSet
					.Bytes([0xB9], strength)                        // mov ecx,strength     ; from the patcher
					.Mark("strengthSet")
					.Bytes(0x0F, 0xAF, 0xC1);                       // imul eax,ecx
			else
				code.Bytes([0x69, 0xC0], strength);                 // imul eax,eax,strength
			code.Bytes(0xF7, 0x6C, 0x24, 0x0C)                      // imul dword [esp+0xC] ; edx:eax = share * speed
				.Bytes([0xB9], 16 * (MaxAccuracy - MinAccuracy) * 100)   // mov ecx,16*(max-min)*100
				.Bytes(0xF7, 0xF9)                                  // idiv ecx
				.Bytes(0x29, 0x44, 0x24, 0x0C)                      // sub [esp+0xC],eax
				.Jump(0xEB, "original");                            // jmp original
		}
		else Original(code);
		return new Hook("Hook.Dribble", sites.Relay, CodeBuilder.JumpPatch(sites.Relay, cave, 6), cave, code.Build());

		CodeBuilder Original(CodeBuilder c) => c
			.Mark("original")
			.Bytes(0x8B, 0x4C, 0x24, 0x0C, 0x8B, 0xC1)              // original: mov ecx,[esp+0xC] ; mov eax,ecx
			.JumpTo(sites.Relay + 6);                               // jmp back
	}

	/// <summary>Stick table direction bit for up (with up-left and up-right).</summary>
	private const byte StickUp = 0x10;

	/// <summary>Test keys (debug): F2 changes the free kick weight, F3 the shot power, F4 the shot aim error, by 10 % (wrapping from the maximum to the minimum).</summary>
	public const byte FreeKickWeightKey = 0x71, ShotPowerKey = 0x72, ShotAccuracyKey = 0x73, SpreadKey = 0x74, SpeedGapKey = 0x75, SprintTurnKey = 0x76, TackleKey = 0x77, PassLeadKey = 0x78;   // VK_F2 to VK_F9
	public const int TestKeyStep = 10;

	/// <summary>Test key of the headers share: F11.</summary>
	public const byte HeadersKey = 0x7A;

	/// <summary>
	/// Shared state of the free kick fix, the shot power and the test keys (<see cref="FreeKickSites.State"/>, zero
	/// padding of .rdata made writable): +0 free kick shot under way, +1 ball from a free kick, +2 test keys set up,
	/// +4 free kick weight percent (word, 0 = the patcher's), +8 tick until which a test key value shows,
	/// +12 KDrawText, +16 shot power percent (word, 0 = the patcher's), +18 test key shown, +20 shot aim error
	/// percent (word, 0 = the patcher's), +22 team spread percent (word, 0xFFFF = none, 0 = the patcher's), +24 speed gap percent (word, 0 = the patcher's),
	/// +26 sprint turn maximum in degrees (word, 0 = the patcher's), +28 challenge reach percent (word, 0 = the patcher's), +30 pass lead percent (word, 0 = the patcher's).
	/// The close dribbling switch state follows (<see cref="DribbleSites.SwitchState"/> = +32): +44 close dribbling strength percent (word, 0 = the patcher's).
	/// +6 headers percent (word, 0 = the patcher's).
	/// </summary>
	private const int FreeKickWeightState = 4, UntilState = 8, DrawTextState = 12, ShotPowerState = 16, ShownState = 18, SetUpState = 2,
		ShotAccuracyState = 20, SpreadState = 22, SpeedGapState = 24, SprintTurnState = 26, TackleState = 28, CloseDribbleStrengthState = 44,
		HeadersState = 6;

	/// <summary>Headers percent read at run time (F11), in the test keys state.</summary>

	/// <summary>Test key of the super shot: F12 (VK_F12).</summary>
	public const byte SuperShotKey = 0x7B;

	/// <summary>F12 changes the super shot height by 2.</summary>
	public const int SuperShotStep = 2;

	/// <summary>Super shot height (word, 0 = the patcher's), in the unused end of the close dribbling state (+46).</summary>
	private const int SuperShotState = 46;

	public static int SuperShotStateAddress(FreeKickSites testKeys) => testKeys.State + SuperShotState;

	/// <summary>A star's super shot is in the air (set at its kick, cleared at the next one): a free byte of the state (+6, beside the free kick weight word).</summary>
	private const int SuperHeavyState = 6;

	public static int SuperShotHeavyAddress(FreeKickSites testKeys) => testKeys.State + SuperHeavyState;

	public static TestKey SuperShotTestKey(int height, int texts, int stride) =>
		new(SuperShotKey, SuperShotState, height, PatchOptions.MinSuperShot, PatchOptions.MaxSuperShot, texts, stride, SuperShotStep);

	/// <summary>
	/// Super shot of the human teams: its <see cref="Speed"/> and <see cref="Height"/>, both in the kick function's
	/// units; <see cref="StateAddress"/> is the F12 word of the height (0 = no test key). <see cref="Weight"/>: extra
	/// ball weight while it flies, in % of the game's, for a shooter whose shot technique stat reaches
	/// <see cref="Star"/> (0 = no extra weight); <see cref="HeavyState"/> is the byte saying such a shot is in the air.
	/// </summary>
	public sealed record SuperShotFix(int Speed, int Height, int StateAddress, int Weight = 0, int Star = 0, int HeavyState = 0);

	/// <summary>
	/// Buttons held in the player (+0xB0) at the kick: L1 (0x400, the lob button) and a right shoulder (0x01000800).
	/// Measured in game with a ring of the last shots: L1 alone gives 0x400, R1 alone 0x01488800, R2 alone 0x01000800,
	/// and L1 + R1 gives 0x01000C00 — holding L1 drops R1's own bits (0x488000), so only the shared part is left.
	/// </summary>
	public const int SuperShotButtons = 0x01000C00;

	/// <summary>Where, in each player record, the shot gauge slowdown keeps the gauge it let through last frame (+0xC0 to +0xDF were always 0 in the measured records).</summary>
	public const int ShotGaugeShadow = 0xD0;

	/// <summary>
	/// Slows the shot gauge's fill: the game adds 8 a frame to <c>[player+0x4D]</c> while the shot button is held (found by watching
	/// it; the instruction that does it was never found). At each player's state dispatch (esi = the player), what the gauge rose by
	/// since the copy kept in the record is cut to <paramref name="percent"/> %, and the copy follows: a gauge that falls (a kick) or
	/// stays is only copied. The caller saves ecx and edx.
	/// </summary>
	internal static void ShotGaugeCore(CodeBuilder code, int percent) =>
		code.Bytes(0x0F, 0xB6, 0x4E, 0x4D)                          // movzx ecx,byte [esi+gauge]
			.Bytes([0x0F, 0xB6, 0x96], ShotGaugeShadow)             // movzx edx,byte [esi+copy]
			.Bytes(0x39, 0xD1)                                      // cmp ecx,edx
			.Jump(0x76, "shotGaugeCopy")                            // jbe shotGaugeCopy
			.Bytes(0x29, 0xD1)                                      // sub ecx,edx             ; the rise
			.Bytes([0x69, 0xC9], (percent * 128 + 50) / 100)        // imul ecx,ecx,percent x 128 / 100
			.Bytes(0x83, 0xC1, 0x40)                                // add ecx,64
			.Bytes(0xC1, 0xE9, 0x07)                                // shr ecx,7               ; rise x percent / 100, rounded
			.Bytes(0x01, 0xD1)                                      // add ecx,edx
			.Bytes(0x88, 0x4E, 0x4D)                                // mov [esi+gauge],cl
			.Mark("shotGaugeCopy")
			.Bytes([0x88, 0x8E], ShotGaugeShadow);                  // mov [esi+copy],cl

	/// <summary>The dispatch's first instruction (<c>movzx eax,[esi+0x16]</c>, 9 bytes with the jump) runs the gauge slowdown first.</summary>
	public static Hook ShotGaugeSlowdown(int cave, ShotGaugeSites sites, int percent)
	{
		var code = new CodeBuilder(cave).Bytes(0x51, 0x52);         // push ecx ; push edx
		ShotGaugeCore(code, percent);
		code.Bytes(0x5A, 0x59)                                      // pop edx ; pop ecx
			.Bytes(0x0F, 0xB6, 0x46, 0x16)                          // movzx eax,byte [esi+0x16]
			.JumpTo(sites.Continuation);                            // the game's state dispatch
		return new Hook("Hook.ShotGauge", sites.Dispatch, CodeBuilder.JumpPatch(sites.Dispatch, cave, 9), cave, code.Build());
	}

	/// <summary>
	/// Super shot (prototype): at the kick function's entry, a shot of a human team whose shooter holds L1 and R1 leaves
	/// at <see cref="SuperShotFix.Speed"/> and <see cref="SuperShotFix.Height"/> (the F12 value when set). A shot is the
	/// player state 4 (standing) or 5 (running: R1 is the sprint button, so most super shots are taken in state 5) with
	/// the kick's 5th argument at 0 — measured in game, a pass from the same states passes 1 there and used to be sent
	/// at the shot's speed.
	/// </summary>
	private static void EmitSuperShot(CodeBuilder code, ShotSites humans, SuperShotFix fix)
	{
		// with the test key, a byte at state+3 collects what was seen at the shots, for a diagnosis: 1 a shot, 8 L1 held,
		// 16 R1 held, 2 both, 4 both and a human team
		byte[] Flag(byte bit) => [0x80, 0x0D, .. BitConverter.GetBytes(fix.StateAddress - SuperShotState + 3), bit];   // or byte [flags],bit
		bool diagnosis = fix.StateAddress != 0;
		code.Bytes(0x60)                                            // pushad                ; the arguments are at esp+0x24...
			.Bytes(0x8B, 0x74, 0x24, 0x24);                         // mov esi,[esp+0x24]    ; player
		if (fix.HeavyState != 0)
			code.Bytes([0xC6, 0x05, .. BitConverter.GetBytes(fix.HeavyState), 0x00]);   // mov byte [heavy],0 ; any kick ends the last one
																						// the shot function is the only caller that makes a shot: a pass reaches this entry from elsewhere (the
																						// physics mod's own call, among others), whatever the player state, and used to be sent at the shot's speed
																						// the block is too long for a short jump to its end: every test that gives up goes through a near jump
		void GiveUp(byte keepGoing, string label) => code.Jump(keepGoing, label).JumpTo("superDone").Mark(label);
		code.Bytes([0x81, 0x7C, 0x24, 0x20], humans.KickCall + 5);  // cmp dword [esp+0x20],shot kick call
		GiveUp(0x74, "superCalled");                                // je superCalled ; jmp superDone
		if (diagnosis) code.Bytes(Flag(1));
		code.Bytes(0x8B, 0x86, 0xB0, 0x00, 0x00, 0x00);             // mov eax,[esi+0xB0]    ; buttons held
		if (diagnosis)
			code.Bytes([0xA9], 0x400).Jump(0x74, "noL1").Bytes(Flag(8)).Mark("noL1")        // test eax,L1
				.Bytes([0xA9], 0x01000800).Jump(0x74, "noR1").Bytes(Flag(16)).Mark("noR1"); // test eax,R1
		code.Bytes([0x25], SuperShotButtons)                        // and eax,L1|R1
			.Bytes([0x3D], SuperShotButtons);                       // cmp eax,L1|R1
		GiveUp(0x74, "superHeld");                                  // je superHeld ; jmp superDone
		if (diagnosis) code.Bytes(Flag(2));
		code.Bytes(0x0F, 0xB6, 0x4E, 0x12, 0x83, 0xE1, 0x01)        // movzx ecx,byte [esi+0x12] ; and ecx,1 ; team
			.Bytes([0x80, 0xB9, .. BitConverter.GetBytes(humans.HumanScores), HumanScore]);  // cmp byte [ecx+scores],HumanScore
		GiveUp(0x73, "superHuman");                                 // jae superHuman ; jmp superDone ; not a human team
		if (diagnosis) code.Bytes(Flag(4));
		// edi = the shooter's stats, read by the speed (shot power) and by the star's weight (shot technique)
		code.Bytes(0x0F, 0xB6, 0x7E, 0x12, 0xC1, 0xE7, 0x05)        // movzx edi,byte [esi+0x12] ; shl edi,5
			.Bytes(0x0F, 0xB6, 0x56, 0x11, 0x03, 0xFA)              // movzx edx,byte [esi+0x11] ; add edi,edx
			.Bytes([0x69, 0xFF], 0x348);                            // imul edi,edi,0x348
		// a good shooter's shot keeps extra weight while it flies: it rises, then dips under the bar. The share of the weight
		// (byte [heavy], 0 to 128 = all of it) grows linearly with the shot technique stat, from none at star - StarRamp to all at star
		if (fix.HeavyState != 0 && fix.Weight > 0)
			code.Bytes([0x0F, 0xB6, 0x87], humans.FavouredFoot + ShotTechniqueStat)   // movzx eax,byte [edi+shot technique]
				.Bytes(0x83, 0xE0, 0x7F)                            // and eax,0x7F
				.Bytes(0x83, 0xE8, (byte)(fix.Star - StarRamp))     // sub eax,star - ramp
				.Jump(0x7E, "superPlain")                           // jle superPlain        ; no weight
				.Bytes(0x6B, 0xC0, 17)                              // imul eax,eax,17      ; x 8.5 once halved: 15 points make 128
				.Bytes(0xD1, 0xE8)                                  // shr eax,1
				.Bytes(0x3D, 128, 0, 0, 0)                          // cmp eax,128
				.Jump(0x76, "superShare")                           // jbe superShare
				.Bytes([0xB8], 128)                                 // mov eax,128
				.Mark("superShare")
				.Bytes([0xA2], fix.HeavyState)                      // mov [heavy],al
				.Mark("superPlain");
		if (fix.StateAddress != 0)
			code.Bytes([0x0F, 0xB7, 0x0D], fix.StateAddress)        // movzx ecx,word [height]
				.Bytes(0x85, 0xC9)                                  // test ecx,ecx
				.Jump(0x75, "superHeight")                          // jne superHeight
				.Bytes([0xB9], fix.Height)                          // mov ecx,height        ; from the patcher
				.Mark("superHeight");
		else
			code.Bytes([0xB9], fix.Height);                         // mov ecx,height
																	// the speed and height arguments both take the chosen values (the lob itself is dropped elsewhere, see LobSuppression)
		// the ball also leaves higher with that share (up to x 1.5, at most the option's maximum): the more it rises, the better it dips
		if (fix.HeavyState != 0 && fix.Weight > 0)
			code.Bytes([0x0F, 0xB6, 0x05], fix.HeavyState)          // movzx eax,byte [heavy]
				.Bytes(0x0F, 0xAF, 0xC1, 0xC1, 0xE8, 0x08)          // imul eax,ecx ; shr eax,8   ; height x share / 256
				.Bytes(0x03, 0xC8)                                  // add ecx,eax
				.Bytes(0x83, 0xF9, (byte)PatchOptions.MaxSuperShot) // cmp ecx,max
				.Jump(0x76, "superLow")                             // jbe superLow
				.Bytes([0xB9], PatchOptions.MaxSuperShot)           // mov ecx,max
				.Mark("superLow");
		// the speed follows the shot power stat: speed x (power + 50) / 149, power capped at 99 (full speed at 99, 67 % at 50)
		code.Bytes([0x0F, 0xB6, 0x87], humans.FavouredFoot + ShotPowerStat)   // movzx eax,byte [edi+shot power]
			.Bytes(0x83, 0xE0, 0x7F)                                // and eax,0x7F
			.Bytes(0x83, 0xF8, 99)                                  // cmp eax,99
			.Jump(0x76, "superPower")                               // jbe superPower
			.Bytes([0xB8], 99)                                      // mov eax,99
			.Mark("superPower")
			.Bytes(0x83, 0xC0, 50)                                  // add eax,50
			.Bytes([0x69, 0xC0], fix.Speed)                         // imul eax,eax,speed
			.Bytes([0xBB], 149)                                     // mov ebx,149
			.Bytes(0x33, 0xD2, 0xF7, 0xF3);                         // xor edx,edx ; div ebx
		EmitSuperShotGauge(code, fix);
		code.Bytes(0x89, 0x44, 0x24, 0x2C)                          // mov [esp+0x2C],eax    ; speed
			.Bytes(0x89, 0x4C, 0x24, 0x30)                          // mov [esp+0x30],ecx    ; height
			.Mark("superDone")
			.Bytes(0x61);                                           // popad
	}

	/// <summary>The shooter's shot gauge, 0 to 0x80 (the game adds 8 per frame while the shot button is held, so it is full in about
	/// 0.25 s, and keeps the value after the release); the kick function still sees it. Measured in game with and without L1 + R1.</summary>
	public const int ShotGaugeField = 0x4D;

	/// <summary>The gauge value at which the super shot is at its best: 108 of 128, about 85 %.</summary>
	private const int SuperGaugeIdeal = 108;

	/// <summary>The height of a barely charged super shot: a flat drive, never a lob.</summary>
	private const int SuperLowHeight = 9;

	/// <summary>
	/// Speed, height and star weight follow the shot gauge (esi = the shooter, eax = the speed and ecx = the height chosen so far;
	/// edx, ebx and edi are free). With g the gauge capped at 128 and 108 the ideal charge:
	/// speed x (0.6 at 0 up to 1 at 108, then down to 0.92 at 128); height from <see cref="SuperLowHeight"/> up to the chosen
	/// height at 108 along a curve that rises fast (1 - (1 - x)^2), then up to 1.31 times it at 128, never above the option's maximum;
	/// the star's weight share x (g / 128)^2, so a soft shot is not weighed down. Whatever the gauge, the speed stays at least
	/// 1.125 times the speed the game gave the same shot without L1 + R1.
	/// </summary>
	private static void EmitSuperShotGauge(CodeBuilder code, SuperShotFix fix)
	{
		code.Bytes(0x0F, 0xB6, 0x56, ShotGaugeField)                // movzx edx,byte [esi+gauge]
			.Bytes([0x81, 0xFA], 128)                               // cmp edx,128
			.Jump(0x76, "gaugeCapped")                              // jbe gaugeCapped
			.Bytes([0xBA], 128)                                     // mov edx,128
			.Mark("gaugeCapped")
			.Bytes([0x81, 0xFA], SuperGaugeIdeal)                   // cmp edx,ideal
			.Jump(0x77, "gaugeOver")                                // ja gaugeOver
			.Bytes(0x6B, 0xDA, 121)                                 // imul ebx,edx,121      ; x 121 / 128 = about 102 / 108
			.Bytes(0xC1, 0xEB, 0x07)                                // shr ebx,7
			.Bytes([0x81, 0xC3], 154)                               // add ebx,154           ; 154 + 102 = 256 at the ideal
			.Jump(0xEB, "gaugeSpeed")                               // jmp gaugeSpeed
			.Mark("gaugeOver")
			.Bytes([0xBB], 256 + SuperGaugeIdeal)                   // mov ebx,256 + ideal
			.Bytes(0x29, 0xD3)                                      // sub ebx,edx           ; 256 - (g - ideal)
			.Mark("gaugeSpeed")
			.Bytes(0x0F, 0xAF, 0xC3)                                // imul eax,ebx
			.Bytes(0xC1, 0xE8, 0x08)                                // shr eax,8             ; speed x factor / 256
			// a super shot is always stronger than the shot the game had worked out (still the argument on the stack): at least 1.125 times it
			.Bytes(0x8B, 0x5C, 0x24, 0x2C)                          // mov ebx,[esp+0x2C]
			.Bytes(0x89, 0xDF, 0xC1, 0xEF, 0x03, 0x01, 0xFB)        // mov edi,ebx ; shr edi,3 ; add ebx,edi
			.Bytes(0x39, 0xD8)                                      // cmp eax,ebx
			.Jump(0x73, "gaugeStronger")                            // jae gaugeStronger
			.Bytes(0x89, 0xD8)                                      // mov eax,ebx
			.Mark("gaugeStronger");
		if (fix.HeavyState != 0 && fix.Weight > 0)
			code.Bytes([0x0F, 0xB6, 0x1D], fix.HeavyState)          // movzx ebx,byte [heavy]
				.Bytes(0x0F, 0xAF, 0xDA, 0xC1, 0xEB, 0x07)          // imul ebx,edx ; shr ebx,7
				.Bytes(0x0F, 0xAF, 0xDA, 0xC1, 0xEB, 0x07)          // imul ebx,edx ; shr ebx,7   ; x (g / 128)^2
				.Bytes([0x88, 0x1D], fix.HeavyState);               // mov [heavy],bl
		code.Bytes([0x69, 0xFA], 151)                               // imul edi,edx,151      ; x = g x 151 / 128 = g / 108 on 0..128
			.Bytes(0xC1, 0xEF, 0x07)                                // shr edi,7
			.Bytes([0x81, 0xFF], 128)                               // cmp edi,128
			.Jump(0x76, "gaugeRise")                                // jbe gaugeRise
			.Bytes([0xBF], 128)                                     // mov edi,128
			.Mark("gaugeRise")
			.Bytes([0xBB], 256)                                     // mov ebx,256
			.Bytes(0x29, 0xFB)                                      // sub ebx,edi
			.Bytes(0x0F, 0xAF, 0xDF)                                // imul ebx,edi
			.Bytes(0xC1, 0xEB, 0x07)                                // shr ebx,7             ; x (256 - x) / 128 = 1 - (1 - x)^2 in 128ths
			.Bytes([0x81, 0xFA], SuperGaugeIdeal)                   // cmp edx,ideal
			.Jump(0x76, "gaugeHeight")                              // jbe gaugeHeight
			.Bytes(0x89, 0xD7)                                      // mov edi,edx
			.Bytes(0x83, 0xEF, SuperGaugeIdeal)                     // sub edi,ideal
			.Bytes(0x8D, 0x1C, 0x7B)                                // lea ebx,[ebx+edi*2]   ; over the ideal: up to 168 / 128
			.Mark("gaugeHeight")
			.Bytes(0x83, 0xF9, SuperLowHeight)                      // cmp ecx,low
			.Jump(0x76, "gaugeLow")                                 // jbe gaugeLow
			.Bytes(0x83, 0xE9, SuperLowHeight)                      // sub ecx,low
			.Bytes(0x0F, 0xAF, 0xCB)                                // imul ecx,ebx
			.Bytes(0xC1, 0xE9, 0x07)                                // shr ecx,7
			.Bytes(0x83, 0xC1, SuperLowHeight)                      // add ecx,low
			.Bytes(0x83, 0xF9, (byte)PatchOptions.MaxSuperShot)     // cmp ecx,max
			.Jump(0x76, "gaugeLow")                                 // jbe gaugeLow
			.Bytes([0xB9], PatchOptions.MaxSuperShot)               // mov ecx,max
			.Mark("gaugeLow");
	}

	/// <summary>Super shot under way: the shooter's address, in a free dword of the human scores block (+0..+3 are the scores and the shot direction).</summary>
	public static int LobFlagAddress(ShotSites humans) => humans.HumanScores + 4;

	/// <summary>
	/// Super shot: the shot must not be a lob, or it keeps the lob's animation and its soft contact (tried in game:
	/// forcing the speed and the height alone still gave a chip). In one cave:
	/// <list type="bullet">
	/// <item><see cref="LobSites.ButtonRebuild"/> ("and ecx,400h", where the buttons are still whole) becomes a jump
	/// here: the flag takes the address of a player holding L1 and a right shoulder (<see cref="SuperShotButtons"/>),
	/// and is cleared only when that same player releases L1. A single shared byte was tried first and let lobs back
	/// in: R1's bits flicker from one frame to the next, and any other player holding L1 cleared it.</item>
	/// <item>each <see cref="LobSites.ButtonReads"/> ("mov eax,[esi+0xB0]" before a test of L1) becomes a call here:
	/// the buttons come back without L1 while the flag is set, so every lob decision — kind, animation, power — takes
	/// the plain shot path. The game's own lob (L1 alone) is untouched.</item>
	/// </list>
	/// </summary>
	public static (byte[] Cave, (int Va, byte[] Bytes)[] Entries) LobSuppression(int cave, LobSites sites, int flag)
	{
		var code = new CodeBuilder(cave)
			.Mark("rebuild")
			.Bytes(0x51)                                            // push ecx
			.Bytes([0x81, 0xE1], SuperShotButtons)                  // and ecx,L1|R1
			.Bytes([0x81, 0xF9], SuperShotButtons)                  // cmp ecx,L1|R1
			.Bytes(0x59)                                            // pop ecx              ; the flags are kept
			.Jump(0x75, "notSuper")                                 // jne notSuper
			.Bytes([0x89, 0x35, .. BitConverter.GetBytes(flag)])    // mov [flag],esi       ; this player's super shot
			.Jump(0xEB, "leave")                                    // jmp leave
			.Mark("notSuper")
			.Bytes([0x3B, 0x35, .. BitConverter.GetBytes(flag)])    // cmp esi,[flag]
			.Jump(0x75, "leave")                                    // jne leave            ; another player: leave it
			.Bytes([0xF7, 0xC1], 0x400)                             // test ecx,L1
			.Jump(0x75, "leave")                                    // jne leave            ; still holding L1: keep it
			.Bytes([0x83, 0x25, .. BitConverter.GetBytes(flag), 0x00])   // and dword [flag],0 ; L1 released
			.Mark("leave")
			.Bytes([0x81, 0xE1], 0x400)                             // original: and ecx,400h
			.JumpTo(sites.ButtonRebuild + 6)                        // jmp back
			.Mark("read")
			.Bytes(0x8B, 0x86, 0xB0, 0x00, 0x00, 0x00)              // original: mov eax,[esi+0xB0]
			.Bytes([0x3B, 0x35, .. BitConverter.GetBytes(flag)])    // cmp esi,[flag]
			.Jump(0x75, "readDone")                                 // jne readDone
			.Bytes([0x25], ~0x400)                                  // and eax,~L1          ; no lob while it lasts
			.Mark("readDone")
			.Bytes(0xC3);                                           // ret                  ; the flags are the caller's test

		var bytes = code.Build();
		var entries = new List<(int, byte[])> { (sites.ButtonRebuild, CodeBuilder.JumpPatch(sites.ButtonRebuild, code.AddressOfLabel("rebuild"), 6)) };
		entries.AddRange(sites.ButtonReads.Select(read => (read, CodeBuilder.CallPatch(read, code.AddressOfLabel("read"), 6))));
		return (bytes, [.. entries]);
	}

	/// <summary>Headers percent read at run time (F11), in the test keys state.</summary>
	public static int HeadersStateAddress(FreeKickSites testKeys) => testKeys.State + HeadersState;

	/// <summary>Pressing challenge percent read at run time (F8), in the test keys state.</summary>
	public static int TackleStateAddress(FreeKickSites testKeys) => testKeys.State + TackleState;

	public static TestKey HeadersTestKey(int percent, int texts, int stride) =>
		new(HeadersKey, HeadersState, percent, PatchOptions.MinHeaders, PatchOptions.MaxHeaders, texts, stride);

	/// <summary>Headers of the human teams: <see cref="Percent"/> % of the game's height; <see cref="StateAddress"/> is the F11 word (0 = no test key).</summary>
	public sealed record HeaderFix(int Percent, int StateAddress);

	/// <summary>Player states (+0x16) of the headers (kick probe). State 48 is not one: it is the corner kick, which follows the corner wait (47).</summary>
	private const byte HeaderStateA = 30, HeaderStateB = 34;

	/// <summary>
	/// Headers of a human team (see <see cref="EmitHumanTeams"/>): at the kick function's entry (player, direction, speed,
	/// height, flag), the height is scaled to a percent (the speed is kept). The AI's headers keep the game's.
	/// </summary>
	private static void EmitHeaders(CodeBuilder code, ShotSites humans, HeaderFix headers)
	{
		code.Bytes(0x60)                                            // pushad                ; the arguments are at esp+0x24...
			.Bytes(0x8B, 0x74, 0x24, 0x24)                          // mov esi,[esp+0x24]    ; player
			.Bytes(0x0F, 0xB6, 0x46, 0x16)                          // movzx eax,byte [esi+0x16] ; state
			.Bytes(0x3C, HeaderStateA).Jump(0x74, "headerState")    // cmp al,30 ; je headerState
			.Bytes(0x3C, HeaderStateB).Jump(0x75, "headerDone")     // cmp al,34 ; jne headerDone
			.Mark("headerState")
			.Bytes(0x0F, 0xB6, 0x4E, 0x12, 0x83, 0xE1, 0x01)        // movzx ecx,byte [esi+0x12] ; and ecx,1 ; team
			.Bytes([0x80, 0xB9, .. BitConverter.GetBytes(humans.HumanScores), HumanScore])   // cmp byte [ecx+scores],HumanScore
			.Jump(0x72, "headerDone");                              // jb headerDone         ; not a human team
		if (headers.StateAddress != 0)
			code.Bytes([0x0F, 0xB7, 0x0D], headers.StateAddress)    // movzx ecx,word [percent]
				.Bytes(0x85, 0xC9)                                  // test ecx,ecx
				.Jump(0x75, "headerPercent")                        // jne headerPercent
				.Bytes([0xB9], headers.Percent)                     // mov ecx,percent       ; from the patcher
				.Mark("headerPercent");
		else
			code.Bytes([0xB9], headers.Percent);                    // mov ecx,percent
																	// only the height: the speed is left alone, so the headers keep their power
		code.Bytes(0x6A, 0x64, 0x5B)                                // push 100 ; pop ebx
			.Bytes(0x8B, 0x44, 0x24, 0x30)                          // mov eax,[esp+0x30]    ; height
			.Bytes(0x0F, 0xAF, 0xC1, 0x99, 0xF7, 0xFB)              // imul eax,ecx ; cdq ; idiv ebx
			.Bytes(0x89, 0x44, 0x24, 0x30)                          // mov [esp+0x30],eax
			.Mark("headerDone")
			.Bytes(0x61);                                           // popad
	}

	/// <summary>Close dribbling strength percent read at run time (F10), in the close dribbling switch state.</summary>
	public static int CloseDribbleStrengthAddress(FreeKickSites testKeys) => testKeys.State + CloseDribbleStrengthState;

	/// <summary>
	/// Easier free kicks: a free kick shot dips sooner after the wall. In one cave:
	/// <list type="bullet">
	/// <item>the set piece's "call shot" becomes a jump here: the call is made with "free kick shot under way" set
	/// when a pad's left stick is pushed up at that moment (the game reads the stick for the shot's spin there too,
	/// and leaves it alone during the flight);</item>
	/// <item>the ball kick function's entry records whether this kick is that shot (so any later touch clears it),
	/// and calls <paramref name="testKeysSetUp"/> (see <see cref="TestKeys"/>) until it is done;</item>
	/// <item>the ball flight's "fadd [weight]" also adds percent * <paramref name="weightUnit"/> (1 % of the game's
	/// weight) while the ball comes from a free kick. The trajectory predictions (keeper, AI) use the same code, so
	/// they see the same dip. The percent is the one set by F2, else <paramref name="percent"/>.</item>
	/// </list>
	/// The game's weight stays where it is (PityCharly's physics mod rewrites it while the game runs).
	/// </summary>
	public static (byte[] Cave, (int Va, byte[] Bytes)[] Entries, int ShotReturn) FreeKick(int cave, FreeKickSites sites, float weightUnit, int percent,
		int testKeysSetUp = 0, ShotSites? humans = null, HeaderFix? headers = null, SuperShotFix? superShot = null)
	{
		int state = sites.State, pending = state, heavy = state + 1, current = state + FreeKickWeightState;
		byte[] Addr(int a) => BitConverter.GetBytes(a);

		var code = new CodeBuilder(cave)
			.Mark("shot")
			.Bytes(0x60)                                            // pushad                ; shot arguments stay below
			.Bytes(0x33, 0xF6)                                      // xor esi,esi           ; pad index
			.Mark("pad")
			.Bytes([0x80, 0x3C, 0xB5, .. Addr(sites.StickTable + 2), 0x00])   // cmp byte [esi*4+magnitude],0
			.Jump(0x74, "nextPad")                                  // je nextPad
			.Bytes([0xF6, 0x04, 0xB5, .. Addr(sites.StickTable + 3), StickUp])   // test byte [esi*4+bits],up
			.Jump(0x75, "stickUp")                                  // jne stickUp
			.Mark("nextPad")
			.Bytes(0x46, 0x83, 0xFE, 0x08)                          // inc esi ; cmp esi,8
			.Jump(0x7C, "pad")                                      // jl pad
			.Jump(0xEB, "padsDone")                                 // jmp padsDone          ; no stick up: the game's own shot
			.Mark("stickUp")
			.Bytes([0xC6, 0x05, .. Addr(pending), 0x01])           // mov byte [pending],1
			.Mark("padsDone")
			.Bytes(0x61)                                            // popad
			.CallTo(sites.Shot)                                     // call shot
			.Mark("shotReturn")
			.Bytes([0xC6, 0x05, .. Addr(pending), 0x00])           // mov byte [pending],0
			.JumpTo(sites.ShotCall + 5)                             // jmp back
			.Mark("kick");
		if (testKeysSetUp != 0) TestKeysSetUpCall(code, sites, testKeysSetUp);
		if (humans != null) EmitHumanTeams(code, humans);
		if (humans != null && headers != null) EmitHeaders(code, humans, headers);
		if (humans != null && superShot != null) EmitSuperShot(code, humans, superShot);
		code.Bytes([0x80, 0x3D, .. Addr(pending), 0x00])           // cmp byte [pending],0
			.Bytes([0x0F, 0x95, 0x05], heavy)                       // setne byte [heavy]
			.Bytes(KickEntryBytes)                                  // original: push ebx/ebp/esi ; mov esi,[esp+0x10]
			.JumpTo(sites.KickEntry + 7)                            // jmp back
			.Mark("weight")
			.Bytes([0xD8, 0x05], sites.Weight)                      // original: fadd dword [weight]
			.Bytes([0x80, 0x3D, .. Addr(heavy), 0x00])             // cmp byte [heavy],0
			.Jump(0x74, "weightDone")                               // je weightDone
			.Bytes(0x50)                                            // push eax
			.Bytes([0x0F, 0xB7, 0x05], current)                     // movzx eax,word [percent]
			.Bytes(0x85, 0xC0)                                      // test eax,eax
			.Jump(0x75, "percentSet")                               // jne percentSet
			.Bytes([0xB8], percent)                                 // mov eax,percent       ; from the patcher
			.Mark("percentSet")
			.Bytes(0x50)                                            // push eax
			.Bytes(0xDB, 0x04, 0x24)                                // fild dword [esp]
			.AddressOf([0xD8, 0x0D], "unit")                        // fmul dword [unit]
			.Bytes(0xDE, 0xC1)                                      // faddp st(1),st
			.Bytes(0x58, 0x58)                                      // pop eax ; pop eax
			.Mark("weightDone");
		if (superShot is { Weight: > 0, HeavyState: not 0 }) SuperShotWeightBlock(code, superShot);
		code.JumpTo(sites.WeightAdd + 6)                            // jmp back
			.Mark("unit")
			.Bytes(BitConverter.GetBytes(weightUnit));

		var bytes = code.Build();
		var entries = new (int, byte[])[]
		{
			(sites.ShotCall, CodeBuilder.JumpPatch(sites.ShotCall, code.AddressOfLabel("shot"), 5)),
			(sites.KickEntry, CodeBuilder.JumpPatch(sites.KickEntry, code.AddressOfLabel("kick"), 7)),
			(sites.WeightAdd, CodeBuilder.JumpPatch(sites.WeightAdd, code.AddressOfLabel("weight"), 6)),
		};
		return (bytes, entries, code.AddressOfLabel("shotReturn"));
	}

	/// <summary>
	/// While a star's super shot flies, the ball flight adds <see cref="SuperShotFix.Weight"/> % of the game's weight
	/// on top of the gravity it already adds, so the shot rises and then dips under the bar. Emitted inside the ball
	/// flight hook, which needs a "unit" label holding 1 % of the game's weight.
	/// </summary>
	private static void SuperShotWeightBlock(CodeBuilder code, SuperShotFix superShot) =>
		code.Bytes([0x80, 0x3D, .. BitConverter.GetBytes(superShot.HeavyState), 0x00])   // cmp byte [heavy],0
			.Jump(0x74, "superWeightDone")                          // je superWeightDone
			.Bytes(0x50)                                            // push eax
			.Bytes([0x0F, 0xB6, 0x05], superShot.HeavyState)        // movzx eax,byte [heavy]   ; share, 128 = all of it
			.Bytes([0x69, 0xC0], superShot.Weight)                  // imul eax,eax,percent
			.Bytes(0xC1, 0xE8, 0x07)                                // shr eax,7
			.Bytes(0x50)                                            // push eax
			.Bytes(0xDB, 0x04, 0x24)                                // fild dword [esp]
			.AddressOf([0xD8, 0x0D], "unit")                        // fmul dword [unit]
			.Bytes(0xDE, 0xC1)                                      // faddp st(1),st
			.Bytes(0x58, 0x58)                                      // pop eax ; pop eax
			.Mark("superWeightDone");

	/// <summary>
	/// The same extra weight, when the free kick fix is off: the ball flight's "fadd [weight]" moves to a cave of
	/// its own, which replays it and adds the super shot's share while a star's shot is in the air.
	/// </summary>
	public static Hook SuperShotWeight(int cave, FreeKickSites sites, float weightUnit, SuperShotFix superShot)
	{
		var code = new CodeBuilder(cave)
			.Bytes([0xD8, 0x05], sites.Weight);                     // original: fadd dword [weight]
		SuperShotWeightBlock(code, superShot);
		var bytes = code.JumpTo(sites.WeightAdd + 6)                // jmp back
			.Mark("unit")
			.Bytes(BitConverter.GetBytes(weightUnit))
			.Build();
		return new Hook("Hook.SuperShot", sites.WeightAdd, CodeBuilder.JumpPatch(sites.WeightAdd, cave, 6), cave, bytes);
	}

	/// <summary>The ball kick function's first 7 bytes: push ebx/ebp/esi ; mov esi,[esp+0x10].</summary>
	private static readonly byte[] KickEntryBytes = [0x53, 0x55, 0x56, 0x8B, 0x74, 0x24, 0x10];

	/// <summary>cmp byte [set up],0 ; jne ; pushad ; call set-up ; popad (the kick function's registers are kept).</summary>
	private static void TestKeysSetUpCall(CodeBuilder code, FreeKickSites sites, int setUp) =>
		code.Bytes([0x80, 0x3D, .. BitConverter.GetBytes(sites.State + SetUpState), 0x00])   // cmp byte [set up],0
			.Jump(0x75, "testKeysReady")                            // jne testKeysReady
			.Bytes(0x60)                                            // pushad
			.CallTo(setUp)                                          // call set-up
			.Bytes(0x61)                                            // popad
			.Mark("testKeysReady");

	/// <summary>
	/// Without the free kick fix, the test keys set-up and the human teams are done from a hook of their own at
	/// the kick function's entry.
	/// </summary>
	public static Hook KickEntryExtras(int cave, int kickEntry, FreeKickSites? testKeys, int setUp, ShotSites? humans, HeaderFix? headers = null, SuperShotFix? superShot = null)
	{
		var code = new CodeBuilder(cave);
		if (testKeys != null && setUp != 0) TestKeysSetUpCall(code, testKeys, setUp);
		if (humans != null) EmitHumanTeams(code, humans);
		if (humans != null && headers != null) EmitHeaders(code, humans, headers);
		if (humans != null && superShot != null) EmitSuperShot(code, humans, superShot);
		var bytes = code.Bytes(KickEntryBytes)                      // original: push ebx/ebp/esi ; mov esi,[esp+0x10]
			.JumpTo(kickEntry + 7)                                  // jmp back
			.Build();
		return new Hook("Hook.ShotPower", kickEntry, CodeBuilder.JumpPatch(kickEntry, cave, 7), cave, bytes);
	}

	/// <summary>A team is controlled by a human once its score reaches this (out of <see cref="MaxHumanScore"/>).</summary>
	public const byte HumanScore = 32, MaxHumanScore = 64;

	/// <summary>
	/// Score gained when a team's active player follows a pad's stick (a miss costs 2). At 3, a human team is known
	/// after about a dozen kicks (at 1, the first shots of a match were not corrected: measured, a shot left at twice
	/// the usual height in the first minute); an AI team, matching a stick about one time in four, still loses ground.
	/// </summary>
	public const byte HumanGain = 3;

	/// <summary>
	/// Human teams, learnt at every kick: for each pad whose stick is pushed, each team's active player gets <see cref="HumanGain"/> when
	/// its input direction is the stick's (or the opposite one: attacking side), -2 otherwise (an AI defender often runs
	/// along or against the human's stick while chasing the ball). A human-controlled team follows its stick nearly always.
	/// The score is the active player's team's (+0x12, as the shots read it), not its slot's: the slots follow the ends.
	/// </summary>
	private static void EmitHumanTeams(CodeBuilder code, ShotSites sites)
	{
		byte[] Addr(int a) => BitConverter.GetBytes(a);
		code.Bytes(0x60)                                            // pushad
			.Bytes(0x33, 0xF6)                                      // xor esi,esi           ; pad
			.Mark("humanPad")
			.Bytes([0x80, 0x3C, 0xB5, .. Addr(sites.StickTable + 2), 0x00])   // cmp byte [esi*4+magnitude],0
			.Jump(0x74, "humanNextPad")                             // je humanNextPad
			.Bytes([0x0F, 0xB6, 0x1C, 0xB5], sites.StickTable + 3)  // movzx ebx,byte [esi*4+bits]
			.Bytes(0x81, 0xE3, 0xF0, 0x00, 0x00, 0x00)              // and ebx,0xF0
			.Bytes(0x8B, 0xD3, 0x8B, 0xCB)                          // mov edx,ebx ; mov ecx,ebx
			.Bytes(0xC1, 0xE2, 0x02, 0xC1, 0xE9, 0x02, 0x0B, 0xD1)  // shl edx,2 ; shr ecx,2 ; or edx,ecx
			.Bytes(0x81, 0xE2, 0xF0, 0x00, 0x00, 0x00)              // and edx,0xF0          ; opposite direction
			.Bytes(0x33, 0xFF)                                      // xor edi,edi           ; team
			.Mark("humanTeam")
			.Bytes([0x0F, 0xB6, 0x04, 0x7D], sites.ActivePlayers)   // movzx eax,byte [edi*2+active]
			.Bytes(0x83, 0xF8, 0x20)                                // cmp eax,32
			.Jump(0x73, "humanNextTeam")                            // jae humanNextTeam     ; none
			.Bytes([0x69, 0xC0], 0x240)                             // imul eax,eax,0x240
																	// the score is the active player's team's (+0x12), as the shots read it: the slots of the active players
																	// follow the sides of the pitch, which the teams swap at half-time
			.Bytes([0x0F, 0xB6, 0xA8], sites.Players + 0x12)        // movzx ebp,byte [eax+players+0x12]
			.Bytes(0x83, 0xE5, 0x01)                                // and ebp,1
			.Bytes([0x0F, 0xB6, 0x88], sites.Players + 0xB0)        // movzx ecx,byte [eax+players+0xB0]
			.Bytes(0x80, 0xE1, 0xF0)                                // and cl,0xF0
			.Bytes(0x3B, 0xCB)                                      // cmp ecx,ebx
			.Jump(0x74, "humanMatch")                               // je humanMatch
			.Bytes(0x3B, 0xCA)                                      // cmp ecx,edx
			.Jump(0x74, "humanMatch")                               // je humanMatch
			.Bytes([0x80, 0xAD, .. Addr(sites.HumanScores), 0x02])  // sub byte [ebp+scores],2 ; a miss costs 2
			.Jump(0x73, "humanNextTeam")                            // jae humanNextTeam
			.Bytes([0xC6, 0x85, .. Addr(sites.HumanScores), 0x00])  // mov byte [ebp+scores],0 ; not below 0
			.Jump(0xEB, "humanNextTeam")                            // jmp humanNextTeam
			.Mark("humanMatch")
			.Bytes([0x80, 0xBD, .. Addr(sites.HumanScores), MaxHumanScore])   // cmp byte [ebp+scores],max
			.Jump(0x73, "humanNextTeam")                            // jae humanNextTeam
			.Bytes([0x80, 0x85, .. Addr(sites.HumanScores), HumanGain])   // add byte [ebp+scores],gain
			.Mark("humanNextTeam")
			.Bytes(0x47, 0x83, 0xFF, 0x02)                          // inc edi ; cmp edi,2
			.Jump(0x7C, "humanTeam")                                // jl humanTeam
			.Mark("humanNextPad")
			.Bytes(0x46, 0x83, 0xFE, 0x08)                          // inc esi ; cmp esi,8
			.Jump(0x7D, "humanDone")                                // jge humanDone
			.JumpTo("humanPad")                                     // jmp humanPad          ; too far for a short jump
			.Mark("humanDone")
			.Bytes(0x61);                                           // popad
	}

	/// <summary>
	/// A test key: <see cref="Key"/> adds <see cref="TestKeyStep"/> to the percent at state + <see cref="State"/>
	/// (starting from <see cref="Default"/>, back to <see cref="Min"/> after <see cref="Max"/>), whose text is
	/// at <see cref="Texts"/> + percent / step * <see cref="Stride"/> (see <see cref="TestKeyTexts"/>).
	/// </summary>
	public sealed record TestKey(byte Key, int State, int Default, int Min, int Max, int Texts, int Stride, int Step = TestKeyStep);

	public static TestKey FreeKickWeightTestKey(int percent, int texts, int stride) =>
		new(FreeKickWeightKey, FreeKickWeightState, percent, PatchOptions.MinFreeKickWeight, PatchOptions.MaxFreeKickWeight, texts, stride);

	public static TestKey SpreadTestKey(int percent, int texts, int stride) =>
		new(SpreadKey, SpreadState, percent, PatchOptions.MinSpread, PatchOptions.MaxSpread, texts, stride);

	/// <summary>
	/// Team spread: the function setting where a player places himself (player, x, z) gets, for the
	/// players placing themselves (state 7) other than the keepers, a target moved away from the centre of their
	/// team's targets across the pitch (width only) by <paramref name="percent"/> % (or the F5 value). The 5 replaced bytes are replayed.
	/// </summary>
	public static Hook Spread(int cave, SpreadSites sites, int percent, FreeKickSites? testKeys = null)
	{
		// the early exits jump back to a copy of the tail at the top (a short jump cannot reach the end)
		var code = new CodeBuilder(cave)
			.Jump(0xEB, "start")                                    // jmp start
			.Mark("done")
			.Bytes(0x61)                                            // popad
			.Bytes(0x56, 0x8B, 0x74, 0x24, 0x08)                    // original: push esi ; mov esi,[esp+8]
			.JumpTo(sites.Entry + 5)                                // jmp back
			.Mark("start")
			.Bytes(0x60)                                            // pushad
			.Bytes(0x8B, 0x74, 0x24, 0x24)                          // mov esi,[esp+0x24]   ; player
			.Bytes(0x80, 0x7E, 0x11, 0x00)                          // cmp byte [esi+0x11],0 ; keeper
			.Jump(0x74, "done")                                     // je done
			.Bytes(0x80, 0x7E, 0x16, 0x07)                          // cmp byte [esi+0x16],7 ; placing himself
			.Jump(0x75, "done")                                     // jne done
			.Bytes(0x0F, 0xB6, 0x5E, 0x12);                         // movzx ebx,byte [esi+0x12] ; team
		if (testKeys != null)
			code.Bytes([0x0F, 0xB7, 0x05], testKeys.State + SpreadState)      // movzx eax,word [percent]
				.Bytes(0x85, 0xC0)                                  // test eax,eax
				.Jump(0x75, "percentSet")                           // jne percentSet
				.Bytes([0xB8], percent)                             // mov eax,percent
				.Mark("percentSet");
		else
			code.Bytes([0xB8], percent);                            // mov eax,percent
		var bytes = code
			.Bytes([0x3D], 0xFFFF)                                  // cmp eax,0xFFFF       ; set to 0 by F5
			.Jump(0x74, "done")                                     // je done
			.Bytes(0x50)                                            // push eax
			.Bytes(0xD9, 0xEE, 0xD9, 0xEE)                          // fldz ; fldz          ; sum x, sum z
			.Bytes(0x33, 0xC9)                                      // xor ecx,ecx          ; count
			.Bytes([0xBF], sites.Players + 0x240)                   // mov edi,players+1
			.Bytes(0x6A, 22, 0x5A)                                  // push 22 ; pop edx
			.Mark("player")
			.Bytes(0x38, 0x5F, 0x12)                                // cmp [edi+0x12],bl    ; same team
			.Jump(0x75, "next")                                     // jne next
			.Bytes(0x80, 0x7F, 0x11, 0x00)                          // cmp byte [edi+0x11],0 ; not the keeper
			.Jump(0x74, "next")                                     // je next
			.Bytes(0xD9, 0x47, 0x6C, 0xDE, 0xC2)                    // fld [edi+0x6C] ; faddp st(2)
			.Bytes(0xD9, 0x47, 0x70, 0xDE, 0xC1)                    // fld [edi+0x70] ; faddp st(1)
			.Bytes(0x41)                                            // inc ecx
			.Mark("next")
			.Bytes([0x81, 0xC7], 0x240)                             // add edi,0x240
			.Bytes(0x4A)                                            // dec edx
			.Jump(0x75, "player")                                   // jnz player
			.Bytes(0x85, 0xC9)                                      // test ecx,ecx
			.Jump(0x74, "empty")                                    // je empty
			.Bytes(0x51, 0xDB, 0x04, 0x24, 0x59)                    // push ecx ; fild [esp] ; pop ecx
			.Bytes(0xDC, 0xFA, 0xDE, 0xF9)                          // fdiv st(2),st ; fdivp st(1),st  ; st0 = centre z, st1 = centre x
			.Bytes(0xDB, 0x04, 0x24)                                // fild dword [esp]     ; percent
			.AddressOf([0xD8, 0x0D], "hundredth")                   // fmul dword [0.01]
			.Bytes(0xD9, 0xE8, 0xDE, 0xC1)                          // fld1 ; faddp         ; st0 = 1 + percent / 100
																	// the length (x) is kept: lines stay at their height, only the width spreads
			.Bytes(0xD9, 0x44, 0x24, 0x30)                          // fld dword [esp+0x30] ; z
			.Bytes(0xD8, 0xE2, 0xD8, 0xC9, 0xD8, 0xC2)              // fsub st,st(2) ; fmul st,st(1) ; fadd st,st(2)
			.Bytes(0xD9, 0x5C, 0x24, 0x30)                          // fstp dword [esp+0x30]
			.Bytes(0xDD, 0xD8)                                      // fstp st(0)           ; factor
			.Mark("empty")
			.Bytes(0xDD, 0xD8, 0xDD, 0xD8)                          // fstp st(0) x 2       ; centre or sums
			.Bytes(0x58)                                            // pop eax
			.Bytes(0x61)                                            // popad
			.Bytes(0x56, 0x8B, 0x74, 0x24, 0x08)                    // original: push esi ; mov esi,[esp+8]
			.JumpTo(sites.Entry + 5)                                // jmp back
			.Mark("hundredth")
			.Bytes(BitConverter.GetBytes(0.01f))
			.Build();
		return new Hook("Hook.Spread", sites.Entry, CodeBuilder.JumpPatch(sites.Entry, cave, 5), cave, bytes);
	}

	/// <summary>Top speed: the stat (above 40, in %) for which the maximum running speed is left as it is (75 / 100).</summary>
	public const int SpeedPivot = (75 - 40) * 100 / 60;

	/// <summary>
	/// Top speed gap: the maximum running speed computed from the top speed stat (7542 + 12 * stat% - 16 * player[0x5C])
	/// gets 12 * stat% scaled by <paramref name="percent"/> %, around <see cref="SpeedPivot"/> (a player at 75 keeps his
	/// speed): fast players get faster, slow ones slower. The 7 replaced bytes (lea ; movzx) are rebuilt.
	/// </summary>
	public static Hook Speed(int cave, SpeedSites sites, int percent, FreeKickSites? testKeys = null)
	{
		int offset = 0x1D76 + 12 * SpeedPivot * (100 - percent) / 100;
		var code = new CodeBuilder(cave);
		if (testKeys != null)
			// with F6: 7542 + 12 * pivot + 12 * (stat% - pivot) * percent / 100, the percent read at run time
			code.Bytes([0x2D], SpeedPivot)                          // sub eax,pivot
				.Bytes([0x0F, 0xB7, 0x0D], testKeys.State + SpeedGapState)   // movzx ecx,word [percent]
				.Bytes(0x85, 0xC9)                                  // test ecx,ecx
				.Jump(0x75, "percentSet")                           // jne percentSet
				.Bytes([0xB9], percent)                             // mov ecx,percent      ; from the patcher
				.Mark("percentSet")
				.Bytes(0x0F, 0xAF, 0xC1, 0x6B, 0xC0, 0x0C)          // imul eax,ecx ; imul eax,eax,12
				.Bytes(0x99)                                        // cdq
				.AddressOf([0xF7, 0x3D], "hundred")                 // idiv dword [100]
				.Bytes([0x05], 0x1D76 + 12 * SpeedPivot);           // add eax,7542+12*pivot
		else
			code.Bytes([0x69, 0xC0], 12 * percent)                  // imul eax,eax,12*percent   ; stat% * 12 * percent
				.Bytes(0x99)                                        // cdq
				.AddressOf([0xF7, 0x3D], "hundred")                 // idiv dword [100]
				.Bytes([0x05], offset);                             // add eax,offset
		var bytes = code
			.Bytes(0x0F, 0xB6, 0x56, 0x4A, 0xC1, 0xE2, 0x04)        // movzx edx,byte [esi+0x4A] ; shl edx,4
			.Bytes(0x2B, 0xC2, 0x8B, 0xD0)                          // sub eax,edx ; mov edx,eax
			.JumpTo(sites.Store)                                    // jmp back to mov [esi+0x46],edx
			.Mark("hundred")
			.Bytes(BitConverter.GetBytes(100))
			.Build();
		return new Hook("Hook.Speed", sites.Entry, CodeBuilder.JumpPatch(sites.Entry, cave, 7), cave, bytes);
	}

	public static TestKey SpeedGapTestKey(int percent, int texts, int stride) =>
		new(SpeedGapKey, SpeedGapState, percent, PatchOptions.MinSpeedGap, PatchOptions.MaxSpeedGap, texts, stride, SpeedGapStep);

	/// <summary>F6 changes the speed gap by 25 % (texts for 0 to 400 % in steps of 10 would not fit a cave).</summary>
	public const int SpeedGapStep = 25;

	/// <summary>The game's sprint turn limit: 0x1000 = 22.5 degrees (an angle is 0x10000 for a full turn).</summary>
	public const int SprintTurnBase = 0x1000;

	/// <summary>
	/// From this turn in 1/256 of a full turn (45 degrees; the game's is 0x50, 112), the sprint stride hands over to
	/// the normal dribble stride, which plays the turning animation instead of a sharp sprint touch.
	/// </summary>
	public const byte SprintTurnAnimationAngle = 0x20;

	/// <summary>F7 changes the sprint turn maximum by 15 degrees.</summary>
	public const int SprintTurnStep = 15;

	public static int Angle(int degrees) => degrees * 0x10000 / 360;

	/// <summary>
	/// Sprint turns: at a sprint stride, the ball's direction turns towards the stick by at most 22.5 degrees. The
	/// limit (push 0x1000) becomes the carrier's: the game's up to <see cref="MinAccuracy"/> in dribble accuracy,
	/// <paramref name="degrees"/> (or the F7 value) from <see cref="MaxAccuracy"/>, in proportion between.
	/// </summary>
	public static Hook SprintTurnLimit(int cave, SprintTurnSites sites, int dribbleAccuracy, int degrees, FreeKickSites? testKeys = null)
	{
		var code = new CodeBuilder(cave)
			.Bytes(0x50, 0x51)                                      // push eax ; push ecx
			.Bytes(0x0F, 0xB6, 0x56, 0x12)                          // movzx edx,byte [esi+0x12] ; team
			.Bytes(0x0F, 0xB6, 0x46, 0x11)                          // movzx eax,byte [esi+0x11] ; slot
			.Bytes(0xC1, 0xE2, 0x05, 0x03, 0xC2)                    // shl edx,5 ; add eax,edx
			.Bytes([0x69, 0xC0], 0x348)                             // imul eax,eax,0x348
			.Bytes([0x8B, 0x80], dribbleAccuracy)                   // mov eax,[eax+dribble accuracy]
			.Bytes(0x83, 0xE0, 0x7F)                                // and eax,0x7F
			.Bytes(0x83, 0xE8, MinAccuracy)                         // sub eax,min
			.Jump(0x7F, "above")                                    // jg above
			.Bytes(0x33, 0xC0)                                      // xor eax,eax          ; weak dribbler: the game's limit
			.Mark("above")
			.Bytes(0x83, 0xF8, MaxAccuracy - MinAccuracy)           // cmp eax,max-min
			.Jump(0x7E, "capped")                                   // jle capped
			.Bytes([0xB8], MaxAccuracy - MinAccuracy)               // mov eax,max-min
			.Mark("capped")
			.Bytes(0x8B, 0xC8);                                     // mov ecx,eax          ; share of the extra turn
		if (testKeys != null)
			code.Bytes([0x0F, 0xB7, 0x05], testKeys.State + SprintTurnState)   // movzx eax,word [degrees]
				.Bytes(0x85, 0xC0)                                  // test eax,eax
				.Jump(0x75, "degreesSet")                           // jne degreesSet
				.Bytes([0xB8], degrees)                             // mov eax,degrees      ; from the patcher
				.Mark("degreesSet")
				.Bytes(0xC1, 0xE0, 0x10, 0x99)                      // shl eax,16 ; cdq
				.Bytes(0x51, 0xB9, 0x68, 0x01, 0x00, 0x00)          // push ecx ; mov ecx,360
				.Bytes(0xF7, 0xF9, 0x59);                           // idiv ecx ; pop ecx   ; angle
		else
			code.Bytes([0xB8], Angle(degrees));                     // mov eax,angle
		var bytes = code
			.Bytes([0x2D], SprintTurnBase)                          // sub eax,0x1000
			.Bytes(0x0F, 0xAF, 0xC1, 0x99)                          // imul eax,ecx ; cdq
			.Bytes(0x6A, MaxAccuracy - MinAccuracy, 0x59)           // push max-min ; pop ecx
			.Bytes(0xF7, 0xF9)                                      // idiv ecx
			.Bytes([0x05], SprintTurnBase)                          // add eax,0x1000
			.Bytes(0x8B, 0xD0, 0x59, 0x58)                          // mov edx,eax ; pop ecx ; pop eax
			.Bytes(0x52)                                            // push edx             ; instead of push 0x1000
			.JumpTo(sites.Limit + 5)                                // jmp back
			.Build();
		return new Hook("Hook.SprintTurn", sites.Limit, CodeBuilder.JumpPatch(sites.Limit, cave, 5), cave, bytes);
	}

	/// <summary>
	/// Sprint touch (<paramref name="site"/>, <paramref name="replaced"/> = the bytes the jump replaces, replayed): a
	/// touch turning the ball more than 22.5 degrees from its current direction also turns the player's run (+0x28)
	/// and facing (+0x26, +0x42) to the touch direction, so he leaves with the ball at the touch instead of finishing
	/// the stride on the old line.
	/// </summary>
	public static Hook SprintTouch(int cave, int site, byte[] replaced, int ball)
	{
		var bytes = new CodeBuilder(cave)
			.Bytes(0x51, 0x52)                                      // push ecx ; push edx
			.Bytes([0x8B, 0x0D], ball)                              // mov ecx,[ball]
			.Bytes(0x0F, 0xB7, 0x49, 0x58)                          // movzx ecx,word [ecx+0x58] ; ball direction
			.Bytes(0x0F, 0xB7, 0x57, 0x30)                          // movzx edx,word [edi+0x30] ; touch direction
			.Bytes(0x2B, 0xD1, 0x0F, 0xBF, 0xD2)                    // sub edx,ecx ; movsx edx,dx
			.Bytes(0x85, 0xD2)                                      // test edx,edx
			.Jump(0x79, "positive")                                 // jns positive
			.Bytes(0xF7, 0xDA)                                      // neg edx
			.Mark("positive")
			.Bytes([0x81, 0xFA], SprintTurnBase)                    // cmp edx,0x1000
			.Jump(0x7E, "done")                                     // jle done             ; the game's turn
			.Bytes(0x66, 0x8B, 0x4F, 0x30)                          // mov cx,[edi+0x30]
			.Bytes(0x66, 0x89, 0x4F, 0x28)                          // mov [edi+0x28],cx    ; run
			.Bytes(0x66, 0x89, 0x4F, 0x26)                          // mov [edi+0x26],cx    ; facing
			.Bytes(0x66, 0x89, 0x4F, 0x42)                          // mov [edi+0x42],cx
			.Mark("done")
			.Bytes(0x5A, 0x59)                                      // pop edx ; pop ecx
			.Bytes(replaced)                                        // the replaced bytes
			.JumpTo(site + replaced.Length)                         // jmp back
			.Build();
		return new Hook("Hook.SprintTurn", site, CodeBuilder.JumpPatch(site, cave, replaced.Length), cave, bytes);
	}

	/// <summary>The first sprint touch's replaced bytes: push eax ; xor eax,eax ; mov ax,[edi+0x30].</summary>
	public static readonly byte[] SprintTouchBytes = [0x50, 0x33, 0xC0, 0x66, 0x8B, 0x47, 0x30];

	/// <summary>The other foot's: xor ecx,ecx ; mov cx,[edi+0x30] ; add esp,0xC.</summary>
	public static readonly byte[] SprintOtherTouchBytes = [0x33, 0xC9, 0x66, 0x8B, 0x4F, 0x30, 0x83, 0xC4, 0x0C];

	/// <summary>
	/// Lofted pass aim: the gauge g (0 to 0x80) gives the distance wanted 0x800 + g * g * 7 / 8 instead of
	/// 0x800 + g / 8 * 0x380 (the same 0x4000 at a full gauge, about 5600 instead of 9200 at half): movzx eax,al ;
	/// imul eax,eax ; imul eax,eax,7 ; shr eax,3, the 12 bytes of the game's conversion.
	/// </summary>
	public static readonly byte[] LoftedGaugeSquare = [0x0F, 0xB6, 0xC0, 0x0F, 0xAF, 0xC0, 0x6B, 0xC0, 0x07, 0xC1, 0xE8, 0x03];

	/// <summary>Lofted pass aim: a receiver must be within this angle of the stick (1/256 of a turn: 22.5 degrees; the game's is 45).</summary>
	public const byte LoftedAngle = 0x10;

	/// <summary>
	/// Pass lead: the two reads of the lead (passer +0xA8) that place the pass point ahead of the receiver jump here and
	/// get it scaled to <paramref name="percent"/> % (or the F9 value), so a through pass no longer runs far ahead of the
	/// receiver (and out on the wings). The lead itself is not changed (the game keeps building it). One cave, two entries.
	/// </summary>
	public static (byte[] Cave, (int Va, byte[] Bytes)[] Entries) PassLead(int cave, PassLeadSites sites, int percent, FreeKickSites? testKeys = null)
	{
		var code = new CodeBuilder(cave);
		void Scaled(string name)
		{
			// eax = lead * percent / 100 (ecx and edx are free at the first read; the second keeps eax and edx)
			code.Mark(name)
				.Bytes(0x8B, 0x87, 0xA8, 0x00, 0x00, 0x00);         // mov eax,[edi+0xA8]   ; lead
			if (testKeys != null)
				code.Bytes([0x0F, 0xB7, 0x0D], testKeys.State + PassLeadState)   // movzx ecx,word [percent]
					.Bytes(0x85, 0xC9)                              // test ecx,ecx
					.Jump(0x75, name + "Set")                       // jne set
					.Bytes(0x6A, (byte)percent, 0x59)               // push percent ; pop ecx ; from the patcher
					.Mark(name + "Set")
					.Bytes(0x0F, 0xAF, 0xC1);                       // imul eax,ecx
			else
				code.Bytes(0x6B, 0xC0, (byte)percent);              // imul eax,eax,percent
			code.Bytes(0x99, 0x6A, 0x64, 0x59, 0xF7, 0xF9);         // cdq ; push 100 ; pop ecx ; idiv ecx
		}
		Scaled("first");
		code.JumpTo(sites.First + 6);                               // jmp back             ; eax = scaled lead
		code.Bytes(0x50, 0x52);                                     // second: push eax ; push edx   ; the cosine stays
		Scaled("second");
		code.Bytes(0x8B, 0xC8, 0x5A, 0x58)                          // mov ecx,eax ; pop edx ; pop eax
			.JumpTo(sites.Second + 6);                              // jmp back             ; ecx = scaled lead
		var bytes = code.Build();
		int second = code.AddressOfLabel("second") - 2;             // the push eax ; push edx before it
		return (bytes, [(sites.First, CodeBuilder.JumpPatch(sites.First, cave, 6)), (sites.Second, CodeBuilder.JumpPatch(sites.Second, second, 6))]);
	}

	internal const int PassLeadState = 30;

	public static TestKey PassLeadTestKey(int percent, int texts, int stride) =>
		new(PassLeadKey, PassLeadState, percent, PatchOptions.MinPassLead, PatchOptions.MaxPassLead, texts, stride);

	/// <summary>Pressing challenge: the game's reach (distance to the ball) and attempt parameter at 100 %.</summary>
	public const int TackleBaseRange = 0xA0, TackleBaseParameter = 0x50;

	/// <summary>F8 changes the challenge reach by 20 % (100 % = the game's).</summary>
	public const int TackleStep = 20;

	/// <summary>The reach and the attempt parameter for <paramref name="percent"/> % (the parameter is pushed as a signed byte).</summary>
	public static (int Range, byte Parameter) TackleValues(int percent) =>
		(TackleBaseRange * percent / 100, (byte)Math.Min(0x7F, TackleBaseParameter * percent / 100));

	/// <summary>
	/// Pressing challenge: the reach test and the attempt parameter (cmp edi,0xA0 ; jge fail ; push 0x50 ; push edi ; push esi
	/// ; call, esi = the pressing player) jump here to scale both by <paramref name="percent"/>, or by the F8 percent read at
	/// <paramref name="stateAddress"/> once pressed (0: no test key). With <paramref name="humans"/>, only a player of a human
	/// team (see <see cref="EmitHumanTeams"/>) gets it: the AI keeps the game's reach, so it no longer takes the ball from a
	/// dribbling star from further (measured in a CPU match at 180 %: 8.6 balls won by a challenge per minute instead of 3.5).
	/// </summary>
	public static Hook Tackle(int cave, TackleSites sites, int percent, int stateAddress, ShotSites? humans)
	{
		var code = new CodeBuilder(cave);
		if (humans != null)
			code.Bytes(0x0F, 0xB6, 0x4E, 0x12, 0x83, 0xE1, 0x01)    // movzx ecx,byte [esi+0x12] ; and ecx,1 ; team
				.Bytes([0x80, 0xB9, .. BitConverter.GetBytes(humans.HumanScores), HumanScore])   // cmp byte [ecx+scores],HumanScore
				.Jump(0x73, "human")                                // jae human
				.Bytes(0x6A, 0x64, 0x58)                            // push 100 ; pop eax    ; the AI: the game's reach
				.Jump(0xEB, "percentSet")                           // jmp percentSet
				.Mark("human");
		if (stateAddress != 0)
			code.Bytes([0x0F, 0xB7, 0x05], stateAddress)            // movzx eax,word [percent]
				.Bytes(0x85, 0xC0)                                  // test eax,eax
				.Jump(0x75, "percentSet");                          // jne percentSet
		var bytes = code
			.Bytes([0xB8], percent)                                 // mov eax,percent      ; from the patcher
			.Mark("percentSet")
			.Bytes(0x8B, 0xC8)                                      // mov ecx,eax
			.Bytes([0x69, 0xC1], TackleBaseRange)                   // imul eax,ecx,0xA0
			.Bytes(0x99)                                            // cdq
			.AddressOf([0xF7, 0x3D], "hundred")                     // idiv dword [100]     ; reach
			.Bytes(0x3B, 0xF8)                                      // cmp edi,eax
			.Jump(0x7C, "inReach")                                  // jl inReach
			.JumpTo(sites.Fail)                                     // jmp fail             ; too far
			.Mark("inReach")
			.Bytes(0x6B, 0xC1, TackleBaseParameter)                 // imul eax,ecx,0x50
			.Bytes(0x99)                                            // cdq
			.AddressOf([0xF7, 0x3D], "hundred")                     // idiv dword [100]
			.Bytes(0x83, 0xF8, 0x7F)                                // cmp eax,0x7F
			.Jump(0x7E, "capped")                                   // jle capped
			.Bytes(0x6A, 0x7F, 0x58)                                // push 0x7F ; pop eax
			.Mark("capped")
			.Bytes(0x50)                                            // push eax             ; instead of push 0x50
			.JumpTo(sites.Range + 10)                               // jmp back to push edi
			.Mark("hundred")
			.Bytes(BitConverter.GetBytes(100))
			.Build();
		return new Hook("Hook.Tackle", sites.Range, CodeBuilder.JumpPatch(sites.Range, cave, 10), cave, bytes);
	}

	public static TestKey TackleTestKey(int percent, int texts, int stride) =>
		new(TackleKey, TackleState, percent, PatchOptions.MinTackle, PatchOptions.MaxTackle, texts, stride, TackleStep);

	public static TestKey SprintTurnTestKey(int degrees, int texts, int stride) =>
		new(SprintTurnKey, SprintTurnState, degrees, PatchOptions.MinSprintTurn, PatchOptions.MaxSprintTurn, texts, stride, SprintTurnStep);

	public static TestKey ShotPowerTestKey(int percent, int texts, int stride) =>
		new(ShotPowerKey, ShotPowerState, percent, PatchOptions.MinShotPower, PatchOptions.MaxShotPower, texts, stride);

	public static TestKey ShotAccuracyTestKey(int percent, int texts, int stride) =>
		new(ShotAccuracyKey, ShotAccuracyState, percent, Math.Max(TestKeyStep, PatchOptions.MinShotAccuracy), PatchOptions.MaxShotAccuracy, texts, stride);   // 0 is the "not set" value

	/// <summary>
	/// Test keys, through Kitserver's kload.dll as the close dribbling switch: the cave starts with the set-up (called
	/// from the kick function's entry until done), which registers a Present hook. On every frame, each key pressed
	/// changes its percent; the last one changed shows for a moment, in yellow under the close dribbling message.
	/// </summary>
	public static byte[] TestKeys(int cave, FreeKickSites sites, IReadOnlyList<TestKey> keys)
	{
		int state = sites.State, until = state + UntilState, drawText = state + DrawTextState, shown = state + ShownState;
		byte[] Addr(int a) => BitConverter.GetBytes(a);
		static byte[] Ascii(string s) => [.. System.Text.Encoding.ASCII.GetBytes(s), 0];

		var code = new CodeBuilder(cave)
			// set-up (cdecl, no argument)
			.Bytes([0xC6, 0x05, .. Addr(state + SetUpState), 0x01])    // mov byte [set up],1
			.Bytes(0x53)                                            // push ebx
			.AddressOf([0x68], "kload")                             // push "kload.dll"
			.Bytes([0xFF, 0x15], sites.ModuleHandleImport)          // call [GetModuleHandleA]
			.Bytes(0x85, 0xC0)                                      // test eax,eax
			.Jump(0x74, "setUpDone")                                // je setUpDone          ; no Kitserver
			.Bytes(0x8B, 0xD8)                                      // mov ebx,eax
			.AddressOf([0x68], "drawTextName")                      // push "KDrawText"
			.Bytes(0x53)                                            // push ebx
			.Bytes([0xFF, 0x15], sites.ProcAddressImport)           // call [GetProcAddress]
			.Bytes(0x85, 0xC0)                                      // test eax,eax
			.Jump(0x74, "setUpDone")                                // je setUpDone
			.Bytes([0xA3], drawText)                                // mov [KDrawText],eax
			.AddressOf([0x68], "hookFunction")                      // push "HookFunction"
			.Bytes(0x53)                                            // push ebx
			.Bytes([0xFF, 0x15], sites.ProcAddressImport)           // call [GetProcAddress]
			.Bytes(0x85, 0xC0)                                      // test eax,eax
			.Jump(0x74, "setUpDone")                                // je setUpDone
			.AddressOf([0x68], "present")                           // push present
			.Bytes(0x6A, 0x03)                                      // push 3                ; hk_D3D_Present
			.Bytes(0xFF, 0xD0)                                      // call eax              ; HookFunction
			.Bytes(0x83, 0xC4, 0x08)                                // add esp,8
			.Mark("setUpDone")
			.Bytes(0x5B, 0xC3)                                      // pop ebx ; ret
																	// Present hook (stdcall, 5 arguments), on every frame
			.Mark("present");
		for (int k = 0; k < keys.Count; k++)
		{
			var key = keys[k];
			code.Bytes(0x6A, key.Key)                               // push key
				.Bytes([0xFF, 0x15], sites.KeyStateImport)          // call [GetAsyncKeyState]
				.Bytes(0xA8, 0x01)                                  // test al,1             ; pressed since last call
				.Jump(0x74, $"next{k}")                             // je next
				.Bytes([0x0F, 0xB7, 0x05], state + key.State)       // movzx eax,word [percent]
				.Bytes(0x85, 0xC0)                                  // test eax,eax
				.Jump(0x75, $"step{k}")                             // jne step
				.Bytes([0xB8], key.Default)                         // mov eax,default
				.Mark($"step{k}")
				.Bytes(0x83, 0xC0, (byte)key.Step)                  // add eax,step
				.Bytes([0x3D], key.Max)                             // cmp eax,max
				.Jump(0x7E, $"stored{k}")                           // jle stored
				.Bytes([0xB8], key.Min)                             // mov eax,min
				.Mark($"stored{k}")
				.Bytes([0x66, 0xA3], state + key.State)             // mov [percent],ax
				.Bytes([0xC6, 0x05, .. Addr(shown), (byte)k])      // mov byte [shown],k
				.Bytes([0xFF, 0x15], sites.TickCountImport)         // call [GetTickCount]
				.Bytes([0x05], SwitchMessageMs)                     // add eax,SwitchMessageMs
				.Bytes([0xA3], until)                               // mov [until],eax
				.Mark($"next{k}");
		}
		code.Bytes([0xFF, 0x15], sites.TickCountImport)             // call [GetTickCount]
			.Bytes([0x3B, 0x05], until)                             // cmp eax,[until]
			.Jump(0x72, "showKey")                                  // jb showKey
			.Bytes(0xC2, 0x14, 0x00)                                // ret 0x14             ; nothing to show
			.Mark("showKey")
			.Bytes([0x0F, 0xB6, 0x0D], shown);                      // movzx ecx,byte [shown]
		for (int k = 0; k < keys.Count; k++)
		{
			var key = keys[k];
			code.Bytes(0x83, 0xF9, (byte)k)                         // cmp ecx,k
				.Jump(0x75, $"notShown{k}")                         // jne notShown
				.Bytes([0x0F, 0xB7, 0x05], state + key.State)       // movzx eax,word [percent]
				.Bytes(0x6A, (byte)key.Step, 0x59)                  // push step ; pop ecx
				.Bytes(0x33, 0xD2, 0xF7, 0xF1)                      // xor edx,edx ; div ecx  ; percent / step
				.Bytes([0x69, 0xC0], key.Stride)                    // imul eax,eax,stride
				.Bytes([0x05], key.Texts)                           // add eax,texts         ; texts[0] = 0 %
				.JumpTo("draw")                                     // jmp draw              ; near: more keys than a short jump spans
				.Mark($"notShown{k}");
		}
		return code.Jump(0xEB, "presentDone")                       // jmp presentDone
			.Mark("draw")
			.Bytes(0x6A, 0x01)                                      // push 1                ; absolute (640 x 480)
			.Bytes(0x50)                                            // push eax              ; text
			.Bytes(0x6A, 0x0C)                                      // push 12               ; font size
			.Bytes([0x68], unchecked((int)0xFFFFE040))              // push yellow
			.Bytes([0x68], BitConverter.ToInt32(BitConverter.GetBytes(64f)))   // push y
			.Bytes([0x68], BitConverter.ToInt32(BitConverter.GetBytes(10f)))   // push x
			.Bytes([0xFF, 0x15], drawText)                          // call [KDrawText]
			.Bytes(0x83, 0xC4, 0x18)                                // add esp,24
			.Mark("presentDone")
			.Bytes(0xC2, 0x14, 0x00)                                // ret 0x14
			.Mark("kload").Bytes(Ascii("kload.dll"))
			.Mark("drawTextName").Bytes(Ascii("KDrawText"))
			.Mark("hookFunction").Bytes(Ascii("HookFunction"))
			.Build();
	}

	/// <summary>Test key texts: one per percent step from 0 %, each padded to the same length (stride).</summary>
	public static (byte[] Bytes, int Stride) TestKeyTexts(IReadOnlyList<string> texts)
	{
		int stride = texts.Max(s => s.Length) + 1;
		return ([.. texts.SelectMany(s => System.Text.Encoding.ASCII.GetBytes(s).Concat(new byte[stride - s.Length]))], stride);
	}

	/// <summary>
	/// Shot power below which the game's powers are left alone: the lowest base of its shot kinds (0xC0).
	/// </summary>
	public const int ShotPowerPivot = 0xC0;

	/// <summary>
	/// Shot power: every shot's power above <see cref="ShotPowerPivot"/> is scaled to a percent, at the shot
	/// function's entry (the power argument), so a short press sends the ball less high. The percent is the one set
	/// by F3 (with <paramref name="testKeys"/>: the state of <see cref="TestKeys"/>), else <paramref name="percent"/>.
	/// Shots from the set piece's call (<see cref="ShotSites.SetPieceReturn"/>, or <paramref name="freeKickReturn"/>
	/// through the free kick cave) keep their power. The replaced 9 bytes are replayed.
	/// </summary>
	/// <summary>
	/// Bytes after the human team scores (<see cref="ShotSites.HumanScores"/>, 2 bytes): the direction asked (word) and
	/// whether the current shot comes from a set piece (byte), both written by <see cref="ShotPower"/>.
	/// </summary>
	public const int AskedOffset = 2, SetPieceOffset = 4;

	public static Hook ShotPower(int cave, ShotSites sites, int percent, int freeKickReturn = 0, FreeKickSites? testKeys = null)
	{
		var code = new CodeBuilder(cave)
			.Bytes(0x8B, 0x44, 0x24, 0x0C)                          // mov eax,[esp+0xC]    ; direction asked
			.Bytes([0x66, 0xA3], sites.HumanScores + AskedOffset)   // mov [asked],ax       ; for ShotAccuracy
			.Bytes([0xC6, 0x05, .. BitConverter.GetBytes(sites.HumanScores + SetPieceOffset), 0x01])   // mov byte [set piece],1
			.Bytes(0x8B, 0x04, 0x24)                                // mov eax,[esp]        ; return address
			.Bytes([0x3D], sites.SetPieceReturn)                    // cmp eax,set piece
			.Jump(0x74, "original");                                // je original
		if (freeKickReturn != 0)
			code.Bytes([0x3D], freeKickReturn)                      // cmp eax,set piece through the free kick cave
				.Jump(0x74, "original");                            // je original
		code.Bytes([0xC6, 0x05, .. BitConverter.GetBytes(sites.HumanScores + SetPieceOffset), 0x00]);   // mov byte [set piece],0
																										// shooter of a team not controlled by a human (see EmitHumanTeams): the game's power
		code.Bytes(0x8B, 0x54, 0x24, 0x04)                          // mov edx,[esp+4]      ; player
			.Bytes(0x0F, 0xB6, 0x4A, 0x12, 0x83, 0xE1, 0x01)        // movzx ecx,byte [edx+0x12] ; and ecx,1 ; team
			.Bytes([0x80, 0xB9, .. BitConverter.GetBytes(sites.HumanScores), HumanScore])   // cmp byte [ecx+scores],HumanScore
			.Jump(0x72, "original");                                // jb original
		code.Bytes(0x8B, 0x44, 0x24, 0x08)                          // mov eax,[esp+8]      ; power
			.Bytes([0x2D], ShotPowerPivot)                          // sub eax,pivot
			.Jump(0x7E, "original");                                // jle original
		if (testKeys != null)
			code.Bytes([0x0F, 0xB7, 0x0D], testKeys.State + ShotPowerState)   // movzx ecx,word [percent]
				.Bytes(0x85, 0xC9)                                  // test ecx,ecx
				.Jump(0x75, "percentSet")                           // jne percentSet
				.Bytes([0xB9], percent)                             // mov ecx,percent       ; from the patcher
				.Mark("percentSet")
				.Bytes(0x0F, 0xAF, 0xC1);                           // imul eax,ecx
		else
			code.Bytes(0x6B, 0xC0, (byte)percent);                  // imul eax,eax,percent
		var bytes = code
			.Bytes(0x99, 0x6A, 0x64, 0x59, 0xF7, 0xF9)              // cdq ; push 100 ; pop ecx ; idiv ecx
			.Bytes([0x05], ShotPowerPivot)                          // add eax,pivot
			.Bytes(0x89, 0x44, 0x24, 0x08)                          // mov [esp+8],eax
			.Mark("original")
			.Bytes(0x83, 0xEC, 0x14)                                // original: sub esp,0x14
			.Bytes([0xD9, 0x05], sites.Float)                       // original: fld dword [...]
			.JumpTo(sites.Entry + 9)                                // jmp back
			.Build();
		return new Hook("Hook.ShotPower", sites.Entry, CodeBuilder.JumpPatch(sites.Entry, cave, 9), cave, bytes);
	}

	/// <summary>The carrier's turn step per frame for an angle in degrees, in 1/65536 of a turn (the game's: 0x100, 1.4 degrees).</summary>
	public static int CarrierTurnStep(int degrees) => (int)Math.Round(degrees * 65536 / 360.0);

	/// <summary>The game's fixed drop of the defensive line, in pitch units (about 11 m).</summary>
	public const int DefenceLineGameDrop = 0xA00;

	/// <summary>What is left of that drop once <paramref name="percent"/> % of it is taken off: 100 % -> 0, 50 % -> 0x500.</summary>
	public static int DefenceLineDrop(int percent) => (int)Math.Round(DefenceLineGameDrop * (100 - percent) / 100.0);

	/// <summary>Off-ball pace: what a player placing himself may always run at, in speed units (about 1100 per m/s, so 1.4 m/s).</summary>
	public const int OffBallPaceBase = 0x600;

	/// <summary>The share of the distance to his place that is added to <see cref="OffBallPaceBase"/>, in 1/256 (100 % -> 256).</summary>
	public static int OffBallPaceScale(int percent) => percent * 256 / 100;

	/// <summary>The player modes left alone: the full-backs' overlaps (9 and 0x0A) and the run beyond the last line (0x0F).</summary>
	private const byte PaceRunModeFirst = 9;

	/// <summary>
	/// Leaves ECX = the cap on the speed of the player at [<paramref name="player"/>] (a ModRM r/m: 6 = esi, 0 = eax), or jumps to
	/// <paramref name="skip"/> when the game is not in open play (restart state not 0), he is not placing himself (state 7) or is on one of the runs: ECX = 0x600 + (|dx| + |dz|) x
	/// scale / 256, dx and dz being the distance to the target (+0x6C, +0x70) from the position (+0xE0, +0xE8). Uses ECX, the x87
	/// stack and 4 bytes below ESP.
	/// </summary>
	private static CodeBuilder OffBallPaceCap(CodeBuilder code, OffBallPaceSites sites, byte player, int percent, string skip) => code
		// open play only: a corner, a free kick, a throw-in, a goal, half-time (restart state not 0) are left to the game (a free
		// kick taker was reported missing the ball with the cap on all the time)
		.Bytes([0x80, 0x3D], sites.RestartState).Bytes(0x00)               // cmp byte [restart],0
		.Jump(0x75, skip)                                                   // jne skip
		.Bytes(0x80, (byte)(0x78 | player), 0x16, 0x07)                    // cmp byte [player+0x16],7 ; placing himself
		.Jump(0x75, skip)                                                   // jne skip
		.Bytes(0x8A, (byte)(0x88 | player), 0xDD, 0x01, 0x00, 0x00)         // mov cl,[player+0x1DD] ; the mode
		.Bytes(0x80, 0xE9, PaceRunModeFirst)                                // sub cl,9
		.Bytes(0x80, 0xF9, 0x01)                                            // cmp cl,1             ; modes 9 and 0x0A
		.Jump(0x76, skip)                                                   // jbe skip
		.Bytes(0x80, 0xF9, 0x0F - PaceRunModeFirst)                         // cmp cl,6             ; mode 0x0F
		.Jump(0x74, skip)                                                   // je skip
		.Bytes(0xD9, (byte)(0x40 | player), 0x6C)                           // fld [player+0x6C]    ; target x
		.Bytes(0xD8, (byte)(0xA0 | player), 0xE0, 0x00, 0x00, 0x00)         // fsub [player+0xE0]   ; - position x
		.Bytes(0xD9, 0xE1)                                                  // fabs
		.Bytes(0xD9, (byte)(0x40 | player), 0x70)                           // fld [player+0x70]
		.Bytes(0xD8, (byte)(0xA0 | player), 0xE8, 0x00, 0x00, 0x00)         // fsub [player+0xE8]
		.Bytes(0xD9, 0xE1, 0xDE, 0xC1)                                      // fabs ; faddp
		.Bytes(0x83, 0xEC, 0x04, 0xDB, 0x1C, 0x24, 0x59)                    // sub esp,4 ; fistp dword [esp] ; pop ecx
		.Bytes([0x69, 0xC9], OffBallPaceScale(percent))                     // imul ecx,ecx,scale
		.Bytes(0xC1, 0xE9, 0x08)                                            // shr ecx,8
		.Bytes([0x81, 0xC1], OffBallPaceBase);                              // add ecx,0x600

	/// <summary>
	/// Off-ball pace, the animation's speed: a player placing himself (state 7) other than on a run gets the speed his running
	/// animation asks for (EDI) capped at 0x600 + <paramref name="percent"/> % of his distance to his place. The 5 replaced bytes are replayed.
	/// </summary>
	public static Hook OffBallPaceAnimation(int cave, OffBallPaceSites sites, int percent)
	{
		var bytes = OffBallPaceCap(new CodeBuilder(cave), sites, 6, percent, "done")
			.Bytes(0x3B, 0xF9)                                              // cmp edi,ecx
			.Jump(0x7E, "done")                                             // jle done
			.Bytes(0x8B, 0xF9)                                              // mov edi,ecx
			.Mark("done")
			.Bytes(0x8B, 0x4E, 0x04, 0x33, 0xD2)                            // original: mov ecx,[esi+4] ; xor edx,edx
			.JumpTo(sites.Animation + 5)
			.Build();
		return new Hook("Hook.OffBallPace", sites.Animation, CodeBuilder.JumpPatch(sites.Animation, cave, 5), cave, bytes);
	}

	/// <summary>
	/// Off-ball pace, the speed stepped towards: at the entry of the function moving the running speed towards a speed (player,
	/// speed aimed at, step), the speed aimed at is capped the same way for a player of the array placing himself. The 6 replaced
	/// bytes (jmp [pointer]) are replayed, and the 6th byte of the entry becomes a nop.
	/// </summary>
	public static Hook OffBallPaceMover(int cave, OffBallPaceSites sites, int percent)
	{
		const int player = 0, arguments = 0x0C, aimed = 0x10;                // esp + 0xC = the player once eax and ecx are saved
		var code = new CodeBuilder(cave)
			.Bytes(0x50, 0x51)                                              // push eax ; push ecx
			.Bytes(0x8B, 0x44, 0x24, arguments)                             // mov eax,[esp+0xC]    ; the player
			.Bytes([0x3D], sites.Players)                                   // cmp eax,players
			.Jump(0x72, "done")                                             // jb done
			.Bytes([0x3D], sites.Players + 23 * 0x240)                      // cmp eax,players + 23 * 0x240
			.Jump(0x73, "done");                                            // jae done
		var bytes = OffBallPaceCap(code, sites, player, percent, "done")
			.Bytes(0x3B, 0x4C, 0x24, aimed)                                 // cmp ecx,[esp+0x10]   ; the speed aimed at
			.Jump(0x7D, "done")                                             // jge done
			.Bytes(0x89, 0x4C, 0x24, aimed)                                 // mov [esp+0x10],ecx
			.Mark("done")
			.Bytes(0x59, 0x58)                                              // pop ecx ; pop eax
			.Bytes([0xFF, 0x25], sites.MoverPointer)                        // original: jmp [pointer]
			.Build();
		return new Hook("Hook.OffBallPace", sites.Mover, CodeBuilder.JumpPatch(sites.Mover, cave, 6), cave, bytes);
	}

	/// <summary>The pad reader's limit for a dead zone in % of the axis (127 from the centre): 18 % -> 23, the game's 29 % -> 37.</summary>
	public static byte StickDeadZoneLimit(int percent) => (byte)Math.Round(percent * 127 / 100.0);

	/// <summary>Set piece aim: stick magnitudes (0-8) under this one leave the aim alone.</summary>
	public const int SetPieceAimFloor = 3;

	/// <summary>Length of the set piece aim code replaced by the hook (see <see cref="StickDeadZoneSites.Aims"/>).</summary>
	public const int SetPieceAimLength = 22;

	/// <summary>Length of the goal kick aim code replaced by the hook (see <see cref="StickDeadZoneSites.GoalKickAims"/>).</summary>
	public const int GoalKickAimLength = 25;

	/// <summary>
	/// Corner, free kick and goal kick aims: instead of a fixed step per frame (0x40, 0x80 for goal kicks) as soon as the
	/// stick leans right or left, the aim ignores a push under <see cref="SetPieceAimFloor"/> (of 8) and then turns by the
	/// magnitude squared (doubled for goal kicks), up to the game's step at full push. Validated in game.
	/// </summary>
	public static Hook SetPieceAim(int cave, int site, bool goalKick = false)
	{
		int length = goalKick ? GoalKickAimLength : SetPieceAimLength;
		var bytes = StickStep(new CodeBuilder(cave)
				.Bytes(0x0F, 0xB6, 0x8E, 0x01, 0x01, 0x00, 0x00))   // movzx ecx,byte [esi+0x101] ; stick magnitude 0-8
			.Bytes(goalKick ? [0xD1, 0xE1] : [])                    // shl ecx,1               ; goal kicks: up to 0x80
			.Bytes(0x84, 0xC0)                                      // test al,al              ; right bit
			.Jump(0x79, "left")                                     // jns left
			.Bytes(0x01, 0x8E, 0xA0, 0x00, 0x00, 0x00)              // add [esi+0xA0],ecx
			.Mark("left")
			.Bytes(0xA8, 0x20)                                      // test al,0x20            ; left bit
			.Jump(0x74, "done")                                     // jz done
			.Bytes(0x29, 0x8E, 0xA0, 0x00, 0x00, 0x00)              // sub [esi+0xA0],ecx
			.Mark("done")
			.JumpTo(site + length)                                  // jmp back (ecx is free: a call follows)
			.Build();
		return new Hook("Hook.StickDeadZone", site, CodeBuilder.JumpPatch(site, cave, length), cave, bytes);
	}

	/// <summary>
	/// ecx = stick magnitude (0-8) → ecx = step (0-64): 0 under <see cref="SetPieceAimFloor"/>, then the magnitude squared.
	/// A magnitude of 0 with a direction bit set comes from the D-pad or the keyboard (the stick table only holds the
	/// analog stick, and its centre zone clears the bits too): full step, as in the game.
	/// </summary>
	private static CodeBuilder StickStep(CodeBuilder code) => code
		.Bytes(0x85, 0xC9)                                          // test ecx,ecx
		.Jump(0x75, "analog")                                       // jnz analog
		.Bytes(0xB1, 0x08)                                          // mov cl,8                ; D-pad: full step
		.Mark("analog")
		.Bytes(0x83, 0xF9, SetPieceAimFloor)                        // cmp ecx,floor
		.Jump(0x73, "push")                                         // jae push
		.Bytes(0x31, 0xC9)                                          // xor ecx,ecx
		.Mark("push")
		.Bytes(0x0F, 0xAF, 0xC9);                                   // imul ecx,ecx            ; 9 .. 64 = 0x40

	/// <summary>Length of the ball placement code replaced by the hook: test bl,bl ; mov edi,-0x40.</summary>
	public const int BallPlacementLength = 7;

	/// <summary>
	/// Training free kick ball placement: the ball moves by 0x40 per frame for each stick direction bit, whatever the push.
	/// The hook sets edi (the game's -0x40) to minus the step of the most pushed stick of the 8 pads
	/// (see <see cref="StickStep"/>); the two "+0x40" moves become "-edi" (<see cref="BallPlacementPlus"/>). Validated in game.
	/// </summary>
	public static Hook BallPlacement(int cave, int site, int stickTable)
	{
		var bytes = StickStep(new CodeBuilder(cave)
			.Bytes(0x31, 0xC9)                                      // xor ecx,ecx
			.Bytes([0xBA], stickTable + 2)                          // mov edx,magnitudes
			.Mark("pad")
			.Bytes(0x0F, 0xB6, 0x02)                                // movzx eax,byte [edx]
			.Bytes(0x39, 0xC8)                                      // cmp eax,ecx
			.Jump(0x76, "next")                                     // jbe next
			.Bytes(0x89, 0xC1)                                      // mov ecx,eax
			.Mark("next")
			.Bytes(0x83, 0xC2, 0x04)                                // add edx,4
			.Bytes([0x81, 0xFA], stickTable + 2 + 8 * 4)            // cmp edx,end             ; 8 pads
			.Jump(0x72, "pad"))                                     // jb pad
			.Bytes(0x89, 0xCF, 0xF7, 0xDF)                          // mov edi,ecx ; neg edi
			.Bytes(0x84, 0xDB)                                      // test bl,bl              ; replaced
			.JumpTo(site + BallPlacementLength)                     // jmp back (eax, ecx, edx are reloaded after)
			.Build();
		return new Hook("Hook.StickDeadZone", site, CodeBuilder.JumpPatch(site, cave, BallPlacementLength), cave, bytes);
	}

	/// <summary>"add word [position],0x40" (8 bytes) → "sub word [position],di ; nop": the same step as the minus moves.</summary>
	public static byte[] BallPlacementPlus(int position) => [0x66, 0x29, 0x3D, .. BitConverter.GetBytes(position), 0x90];

	/// <summary>Shot height above which the excess is scaled by <see cref="ShotAccuracy"/>.</summary>
	public const int ShotHeightPivot = 20;

	/// <summary>
	/// A lob is a shot whose shooter holds this button (L1) when the ball leaves (player+0xB0): its height is kept at
	/// <see cref="LobHeight"/> % instead of being taken for an error (tested in game: a lob leaves at a height of 45 to
	/// 68, a shot at 13 to 35).
	/// </summary>
	public const int LobButton = 0x400;
	public const byte LobHeight = 95;

	/// <summary>
	/// Shot accuracy stat (7 bits, byte at this offset of a player's stats), the "Précis." / shot accuracy shown in game.
	/// Up to 75 the accuracy share is kept; above, it shrinks by 4 % a point (50 % at 88, 20 % at 95), never below
	/// <see cref="AccuracyFloor"/> %: a top striker under pressure no longer sends the ball into the stands. The game
	/// draws its misses, those under pressure included (0x545C20, 0x5B9CB1), against the shot technique at +0x12, which
	/// the patcher read by mistake until 1.26 (checked against the stats shown in game on 2026-11-15).
	/// </summary>
	public const int ShotAccuracyStat = 0x10;
	public const byte AccuracyFloor = 20;

	/// <summary>Shot power (+0x11, to check against the player screen) and shot technique (+0x12) stats, read by the super shot.</summary>
	public const int ShotPowerStat = 0x11, ShotTechniqueStat = 0x12;

	/// <summary>Points of shot technique below the star's, from which the ball's extra weight starts to grow (none at 75 for a star of 90).</summary>
	public const int StarRamp = 15;

	/// <summary>
	/// Shot accuracy: the shot function's "call kick" goes through here. For a team controlled by a human (see
	/// <see cref="EmitHumanTeams"/>), the gap between the direction asked (saved by <see cref="ShotPower"/>) and the
	/// direction after the game's random penalties is scaled to <paramref name="percent"/>, and so is the height
	/// above <see cref="ShotHeightPivot"/>. A shot from the weak foot (the game gives it a larger gap, always to the
	/// same side) keeps percent² / 100 instead: 25 % at 50 %. A lob (see <see cref="LobButton"/>) keeps its height
	/// and gets <paramref name="lobPower"/> % of its speed. Then the kick function is called as before. With the
	/// test key, F4's percent is used once set. Set pieces (flagged by <see cref="ShotPower"/>) keep the game's shot:
	/// with their height scaled down, free kicks could no longer clear the wall.
	/// </summary>
	public static Hook ShotAccuracy(int cave, ShotSites sites, int percent, FreeKickSites? testKeys = null, int lobPower = 100)
	{
		var code = new CodeBuilder(cave)
			.Bytes(0x8B, 0x54, 0x24, 0x04)                          // mov edx,[esp+4]      ; player
			.Bytes(0x0F, 0xB6, 0x4A, 0x12, 0x83, 0xE1, 0x01)        // movzx ecx,byte [edx+0x12] ; and ecx,1 ; team
			.Bytes([0x80, 0xB9, .. BitConverter.GetBytes(sites.HumanScores), HumanScore])   // cmp byte [ecx+scores],HumanScore
			.Jump(0x72, "game")                                     // jb game              ; AI: the game's shot
			.Bytes([0x80, 0x3D, .. BitConverter.GetBytes(sites.HumanScores + SetPieceOffset), 0x00])   // cmp byte [set piece],0
			.Jump(0x74, "human")                                    // je human
			.Mark("game")                                           // set pieces too: their height clears the wall
			.JumpTo(sites.KickEntry)                                // jmp kick function
			.Mark("human");
		// the percent stays on the stack: the arguments move 4 bytes up
		if (testKeys != null)
			code.Bytes([0x0F, 0xB7, 0x0D], testKeys.State + ShotAccuracyState)   // movzx ecx,word [percent]
				.Bytes(0x85, 0xC9)                                  // test ecx,ecx
				.Jump(0x75, "percentSet")                           // jne percentSet
				.Bytes(0x6A, (byte)percent, 0x59)                   // push percent ; pop ecx ; from the patcher
				.Mark("percentSet")
				.Bytes(0x51);                                       // push ecx
		else
			code.Bytes(0x6A, (byte)percent);                        // push percent
		var bytes = code
			// weak foot (the animation's foot is not the favoured one): percent = percent * percent / 100
			.Bytes(0x8B, 0x54, 0x24, 0x08)                          // mov edx,[esp+8]      ; player
			.Bytes(0x8B, 0x4A, 0x04, 0x0F, 0xB6, 0x49, 0x2D)        // mov ecx,[edx+4] ; movzx ecx,byte [ecx+0x2D] ; foot
			.Bytes(0x0F, 0xB6, 0x42, 0x12, 0xC1, 0xE0, 0x05)        // movzx eax,byte [edx+0x12] ; shl eax,5
			.Bytes(0x0F, 0xB6, 0x52, 0x11, 0x03, 0xC2)              // movzx edx,byte [edx+0x11] ; add eax,edx
			.Bytes([0x69, 0xC0], 0x348)                             // imul eax,eax,0x348
			.Bytes([0x8B, 0x80], sites.FavouredFoot)                // mov eax,[eax+favoured foot]
			.Bytes(0x83, 0xE0, 0x01, 0x3B, 0xC1)                    // and eax,1 ; cmp eax,ecx
			.Jump(0x74, "favouredFoot")                             // je favouredFoot
			.Bytes(0x8B, 0x04, 0x24, 0x0F, 0xAF, 0xC0)              // mov eax,[esp] ; imul eax,eax
			.Bytes(0x6A, 0x64, 0x59, 0x99, 0xF7, 0xF9)              // push 100 ; pop ecx ; cdq ; idiv ecx
			.Bytes(0x89, 0x04, 0x24)                                // mov [esp],eax
			.Mark("favouredFoot")
			// the shooter's shot accuracy (the stat the game draws its misses against, pressure included): up to
			// AccuracyFull the share is kept; above, it shrinks by 4 % a point, down to AccuracyFloor %
			.Bytes(0x8B, 0x54, 0x24, 0x08)                          // mov edx,[esp+8]      ; player
			.Bytes(0x0F, 0xB6, 0x42, 0x12, 0xC1, 0xE0, 0x05)        // movzx eax,byte [edx+0x12] ; shl eax,5
			.Bytes(0x0F, 0xB6, 0x52, 0x11, 0x03, 0xC2)              // movzx edx,byte [edx+0x11] ; add eax,edx
			.Bytes([0x69, 0xC0], 0x348)                             // imul eax,eax,0x348
			.Bytes([0x0F, 0xB6, 0x80], sites.FavouredFoot + ShotAccuracyStat)   // movzx eax,byte [eax+shot accuracy]
			.Bytes(0x83, 0xE0, 0x7F, 0xF7, 0xD8, 0x83, 0xC0, 0x64)  // and eax,0x7F ; neg eax ; add eax,100
			.Bytes(0xC1, 0xE0, 0x02)                                // shl eax,2            ; 4 * (100 - accuracy)
			.Bytes(0x83, 0xF8, 0x64)                                // cmp eax,100
			.Jump(0x7E, "belowFull")                                // jle belowFull
			.Bytes(0x6A, 0x64, 0x58)                                // push 100 ; pop eax
			.Mark("belowFull")
			.Bytes(0x83, 0xF8, AccuracyFloor)                       // cmp eax,floor
			.Jump(0x7D, "aboveFloor")                               // jge aboveFloor
			.Bytes(0x6A, AccuracyFloor, 0x58)                       // push floor ; pop eax
			.Mark("aboveFloor")
			.Bytes(0x0F, 0xAF, 0x04, 0x24)                          // imul eax,[esp]       ; percent
			.Bytes(0x6A, 0x64, 0x59, 0x99, 0xF7, 0xF9)              // push 100 ; pop ecx ; cdq ; idiv ecx
			.Bytes(0x89, 0x04, 0x24)                                // mov [esp],eax
																	// direction: asked + (direction - asked) * percent / 100, on 16 bits
			.Bytes([0x0F, 0xB7, 0x0D], sites.HumanScores + AskedOffset)   // movzx ecx,word [asked]
			.Bytes(0x8B, 0x44, 0x24, 0x0C)                          // mov eax,[esp+0xC]    ; direction
			.Bytes(0x2B, 0xC1, 0x98)                                // sub eax,ecx ; cwde   ; signed 16-bit gap
			.Bytes(0x0F, 0xAF, 0x04, 0x24)                          // imul eax,[esp]       ; percent
			.Bytes(0x99, 0x6A, 0x64, 0xF7, 0x3C, 0x24, 0x5A)        // cdq ; push 100 ; idiv dword [esp] ; pop edx
			.Bytes(0x03, 0xC1, 0x0F, 0xB7, 0xC0)                    // add eax,ecx ; movzx eax,ax
			.Bytes(0x89, 0x44, 0x24, 0x0C)                          // mov [esp+0xC],eax
																	// a lob (L1 held when the ball leaves): its height is wanted, not an error; it keeps LobHeight % of it,
																	// and its speed is scaled to lobPower %
			.Bytes(0x8B, 0x44, 0x24, 0x08)                          // mov eax,[esp+8]      ; player
			.Bytes([0xF7, 0x80, 0xB0, 0x00, 0x00, 0x00], LobButton) // test dword [eax+0xB0],lob button
			.Jump(0x74, "notLob")                                   // jz notLob
			.Bytes(0x8B, 0x44, 0x24, 0x10, 0x6B, 0xC0, (byte)lobPower)   // mov eax,[esp+0x10] ; imul eax,eax,lobPower ; speed
			.Bytes(0x99, 0x6A, 0x64, 0x59, 0xF7, 0xF9)              // cdq ; push 100 ; pop ecx ; idiv ecx
			.Bytes(0x89, 0x44, 0x24, 0x10)                          // mov [esp+0x10],eax
			.Bytes(0x8B, 0x44, 0x24, 0x14, 0x6B, 0xC0, LobHeight)   // mov eax,[esp+0x14] ; imul eax,eax,LobHeight ; height
			.Bytes(0x99, 0xF7, 0xF9)                                // cdq ; idiv ecx
			.Bytes(0x89, 0x44, 0x24, 0x14)                          // mov [esp+0x14],eax
			.Jump(0xEB, "done")                                     // jmp done
			.Mark("notLob")
			// height above the pivot: scaled the same way
			.Bytes(0x8B, 0x44, 0x24, 0x14)                          // mov eax,[esp+0x14]   ; height
			.Bytes([0x2D], ShotHeightPivot)                         // sub eax,pivot
			.Jump(0x7E, "done")                                     // jle done
			.Bytes(0x0F, 0xAF, 0x04, 0x24)                          // imul eax,[esp]       ; percent
			.Bytes(0x99, 0x6A, 0x64, 0xF7, 0x3C, 0x24, 0x5A)        // cdq ; push 100 ; idiv dword [esp] ; pop edx
			.Bytes([0x05], ShotHeightPivot)                         // add eax,pivot
			.Bytes(0x89, 0x44, 0x24, 0x14)                          // mov [esp+0x14],eax
			.Mark("done")
			.Bytes(0x59)                                            // pop ecx              ; percent
			.Mark("kick")
			.JumpTo(sites.KickEntry)                                // jmp kick function    ; returns to the shot function
			.Build();
		return new Hook("Hook.ShotPower", sites.KickCall, CodeBuilder.JumpPatch(sites.KickCall, cave, 5) is var j ? [0xE8, .. j[1..]] : [], cave, bytes);
	}

	/// <summary>Close dribbling message: shown this long after a switch (and at the first touch of a match).</summary>
	public const int SwitchMessageMs = 2000, FirstMessageMs = 2500;

	/// <summary>
	/// Close dribbling switch message, through Kitserver's kload.dll (HookFunction, KDrawText).
	/// The cave starts with the set-up, called once from the kick relay: it registers a Present hook
	/// (called on every frame) that reads F1, switches close dribbling, and draws <paramref name="on"/>
	/// in green or <paramref name="off"/> in red for a moment. Without kload.dll, nothing is registered
	/// and the relay keeps reading F1 itself. Texts are ASCII (Kitserver's font).
	/// </summary>
	public static byte[] SwitchMessage(int cave, DribbleSites sites, string on, string off)
	{
		int state = sites.SwitchState;
		byte[] Ascii(string s) => [.. System.Text.Encoding.ASCII.GetBytes(s), 0];
		byte[] Float(float f) => BitConverter.GetBytes(f);

		return new CodeBuilder(cave)
			// set-up (cdecl, no argument; eax, ecx, edx free)
			.Bytes([0xC6, 0x05, .. BitConverter.GetBytes(state + 1), 0x01])   // mov byte [set up],1
			.Bytes(0x53)                                            // push ebx
			.AddressOf([0x68], "kload")                             // push "kload.dll"
			.Bytes([0xFF, 0x15], sites.ModuleHandleImport)          // call [GetModuleHandleA]
			.Bytes(0x85, 0xC0)                                      // test eax,eax
			.Jump(0x74, "setUpDone")                                // je setUpDone          ; no Kitserver
			.Bytes(0x8B, 0xD8)                                      // mov ebx,eax
			.AddressOf([0x68], "drawText")                          // push "KDrawText"
			.Bytes(0x53)                                            // push ebx
			.Bytes([0xFF, 0x15], sites.ProcAddressImport)           // call [GetProcAddress]
			.Bytes(0x85, 0xC0)                                      // test eax,eax
			.Jump(0x74, "setUpDone")                                // je setUpDone
			.Bytes([0xA3], state + 4)                               // mov [KDrawText],eax
			.AddressOf([0x68], "hookFunction")                      // push "HookFunction"
			.Bytes(0x53)                                            // push ebx
			.Bytes([0xFF, 0x15], sites.ProcAddressImport)           // call [GetProcAddress]
			.Bytes(0x85, 0xC0)                                      // test eax,eax
			.Jump(0x74, "setUpDone")                                // je setUpDone
			.AddressOf([0x68], "present")                           // push present
			.Bytes(0x6A, 0x03)                                      // push 3                ; hk_D3D_Present
			.Bytes(0xFF, 0xD0)                                      // call eax              ; HookFunction
			.Bytes(0x83, 0xC4, 0x08)                                // add esp,8
			.Bytes([0xC6, 0x05, .. BitConverter.GetBytes(state + 2), 0x01])   // mov byte [message hook],1
			.Bytes([0xFF, 0x15], sites.TickCountImport)             // call [GetTickCount]
			.Bytes([0x05], FirstMessageMs)                          // add eax,FirstMessageMs ; show the state at kick-off
			.Bytes([0xA3], state + 8)                               // mov [until],eax
			.Mark("setUpDone")
			.Bytes(0x5B, 0xC3)                                      // pop ebx ; ret
																	// Present hook (stdcall, 5 arguments), on every frame
			.Mark("present")
			.Bytes(0x6A, CloseDribbleSwitchKey)                     // push VK_F1
			.Bytes([0xFF, 0x15], sites.KeyStateImport)              // call [GetAsyncKeyState]
			.Bytes(0xA8, 0x01)                                      // test al,1
			.Jump(0x74, "show")                                     // je show
			.Bytes([0x80, 0x35, .. BitConverter.GetBytes(state), 0x01])       // xor byte [off],1
			.Bytes([0xFF, 0x15], sites.TickCountImport)             // call [GetTickCount]
			.Bytes([0x05], SwitchMessageMs)                         // add eax,SwitchMessageMs
			.Bytes([0xA3], state + 8)                               // mov [until],eax
			.Mark("show")
			.Bytes([0xFF, 0x15], sites.TickCountImport)             // call [GetTickCount]
			.Bytes([0x3B, 0x05], state + 8)                         // cmp eax,[until]
			.Jump(0x73, "presentDone")                              // jae presentDone
			.Bytes(0x6A, 0x01)                                      // push 1                ; absolute (640 x 480)
			.Bytes([0x80, 0x3D, .. BitConverter.GetBytes(state), 0x00])       // cmp byte [off],0
			.Jump(0x75, "offText")                                  // jne offText
			.AddressOf([0x68], "on")                                // push on               ; text
			.Bytes([0xB8], unchecked((int)0xFF40FF40))              // mov eax,green
			.Jump(0xEB, "draw")                                     // jmp draw
			.Mark("offText")
			.AddressOf([0x68], "off")                               // push off              ; text
			.Bytes([0xB8], unchecked((int)0xFFFF4040))              // mov eax,red
			.Mark("draw")
			.Bytes(0x6A, 0x0C)                                      // push 12               ; font size
			.Bytes(0x50)                                            // push eax              ; color
			.Bytes([0x68], BitConverter.ToInt32(Float(48f)))        // push y
			.Bytes([0x68], BitConverter.ToInt32(Float(10f)))        // push x
			.Bytes([0xFF, 0x15], state + 4)                         // call [KDrawText]
			.Bytes(0x83, 0xC4, 0x18)                                // add esp,24
			.Mark("presentDone")
			.Bytes(0xC2, 0x14, 0x00)                                // ret 0x14
			.Mark("kload").Bytes(Ascii("kload.dll"))
			.Mark("drawText").Bytes(Ascii("KDrawText"))
			.Mark("hookFunction").Bytes(Ascii("HookFunction"))
			.Mark("on").Bytes(Ascii(on))
			.Mark("off").Bytes(Ascii(off))
			.Build();
	}
}
