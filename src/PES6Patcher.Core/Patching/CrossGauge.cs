namespace PES6Patcher.Core.Patching;

/// <summary>
/// Cross gauge: a human player's cross (player state 9, circle from a wing) is held while the circle stays down and its power follows
/// how long it was held, instead of being the game's automatic one. The gauge (the player's byte +0x4D, 0 to 0x80) fills in
/// <see cref="PatchOptions.CrossGaugeMs"/> milliseconds on the game's own clock (GetTickCount); the cross starts when the circle is let go,
/// or at once when the gauge is full. The cross is aimed with the left stick, or where the player faces when no direction is held: it goes to the
/// team-mate in that direction whose distance suits the gauge, else to the point at the gauge's distance in that direction.
/// </summary>
/// <remarks>
/// The cross is started by one function called from three places. Each call goes through a relay: for a human (pad number below 8) whose
/// circle (bit 0x2000 of +0xB0) is held and whose gauge is not full, the call is skipped, the gauge is updated and the game's wait counter
/// is kept short so that it asks again at the next frame; otherwise the call is made. Another hook at the start of the wait phase sets that
/// counter so that a cross does not wait the game's 7 frames, and one more does the same in the planner, which starts the cross asked on the run. The power the game asks is replaced by one that follows the gauge, and the
/// stat's cap is lifted by the gauge, up to the kick function's own ceiling (0x9000).
/// The aim is one more hook, in the cross start right after the game chose the receiver: it replaces the receiver (+0x48) and the aimed
/// point (+0x9C, +0xA0), from which the game takes the kick's animation and direction.
/// State (the fill time, then 8 bytes a pad: start and last call of the hold) lives in the end of .rdata.
/// </remarks>
internal static class CrossGauge
{
	/// <summary>Power at gauge 0, and what the gauge adds up to its maximum (0x4000 + 0x5000 = 0x9000, the kick function's ceiling).</summary>
	private const int PowerMinimum = 0x4000, PowerSpan = 0x5000;

	/// <summary>What a full gauge adds to the stat's cap.</summary>
	private const int CapBonus = 0x3000;

	/// <summary>The game waits about two frames before the first call of the retained cross: the clock starts that much earlier.</summary>
	private const int HeadStartMs = 33;

	/// <summary>Bytes of state: the fill time, then 8 pads of 8 bytes.</summary>
	internal const int StateSize = 4 + 8 * 8;

	/// <summary>The helper: updates the gauge of the player in esi from the time his circle has been held (registers and flags kept).</summary>
	private static byte[] Helper(int at, CrossGaugeSites sites)
	{
		int fillMs = sites.State, slots = sites.State + 4;
		var code = new CodeBuilder(at);
		code.Bytes(0x60, 0x9C);                                                         // pushad ; pushfd
		code.Bytes([0xFF, 0x15], sites.TickCount);                                      // call [GetTickCount]
		code.Bytes(0x0F, 0xB6, 0x4E, 0x47);                                             // movzx ecx, byte [esi+0x47]   (pad number)
		code.Bytes(0xC1, 0xE1, 0x03);                                                   // shl ecx, 3
		code.Bytes([0x81, 0xC1], slots);                                                // add ecx, slots   (start, last call: 8 bytes a pad)
		code.Bytes(0x8B, 0x51, 0x04);                                                   // mov edx, [ecx+4]
		code.Bytes(0x85, 0xD2);                                                         // test edx, edx
		code.Jump(0x74, "restart");                                                     // jz .restart
		code.Bytes(0x89, 0xC3);                                                         // mov ebx, eax
		code.Bytes(0x29, 0xD3);                                                         // sub ebx, edx   (time since the last call)
		code.Bytes(0x81, 0xFB, 0x64, 0x00, 0x00, 0x00);                                 // cmp ebx, 100
		code.Jump(0x77, "restart");                                                     // ja .restart   (a new hold)
		code.Bytes(0x8B, 0x19);                                                         // mov ebx, [ecx]
		code.Jump(0xEB, "have");                                                        // jmp .have
		code.Mark("restart");
		code.Bytes(0x8D, 0x58, unchecked((byte)-HeadStartMs));                          // lea ebx, [eax-head start]
		code.Bytes(0x89, 0x19);                                                         // mov [ecx], ebx
		code.Mark("have");
		code.Bytes(0x89, 0x41, 0x04);                                                   // mov [ecx+4], eax
		code.Bytes(0x29, 0xD8);                                                         // sub eax, ebx   (time held)
		code.Bytes(0xC1, 0xE0, 0x07);                                                   // shl eax, 7
		code.Bytes(0x31, 0xD2);                                                         // xor edx, edx
		code.Bytes([0x8B, 0x0D], fillMs);                                               // mov ecx, [fill time]
		code.Bytes(0xF7, 0xF1);                                                         // div ecx
		code.Bytes(0x3D, 0x80, 0x00, 0x00, 0x00);                                       // cmp eax, 0x80
		code.Jump(0x76, "ok");                                                          // jbe .ok
		code.Bytes(0xB8, 0x80, 0x00, 0x00, 0x00);                                       // mov eax, 0x80
		code.Mark("ok");
		code.Bytes(0x88, 0x46, 0x4D);                                                   // mov [esi+0x4D], al
		code.Bytes(0x9D, 0x61);                                                         // popfd ; popad
		code.Bytes(0xC3);                                                               // ret
		return code.Build();
	}

	/// <summary>
	/// The relay of one call of the cross start. The call is skipped while a human holds the circle and the gauge is not full; the counter
	/// of the game's wait (<paramref name="reset"/>) is kept at 7, so that the game asks again at the next frame.
	/// </summary>
	private static byte[] Relay(int at, int helper, int back, int startCross, byte[] reset)
	{
		var code = new CodeBuilder(at);
		code.Bytes(0x80, 0x7E, 0x47, 0x08);                                             // cmp byte [esi+0x47], 8
		code.Jump(0x73, "go");                                                          // jae .go   (not a human)
		code.Bytes(0xF7, 0x86, 0xB0, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00);         // test dword [esi+0xB0], 0x2000   (circle held)
		code.Jump(0x74, "go");                                                          // jz .go
		code.CallTo(helper);                                                            // update the gauge
		code.Bytes(0x80, 0x7E, 0x4D, 0x80);                                             // cmp byte [esi+0x4D], 0x80
		code.Jump(0x73, "go");                                                          // jae .go   (full: start the cross)
		code.Bytes(reset);                                                              // keep the wait counter at 7
		code.JumpTo(back);                                                              // skip the call
		code.Mark("go");
		code.CallTo(startCross);
		code.JumpTo(back);
		return code.Build();
	}

	/// <summary>Wait phase: the counter jumps to 7 so that the increment makes 8, the value at which the game goes on to the call.</summary>
	private static byte[] Phase(int at, CrossGaugeSites sites)
	{
		var code = new CodeBuilder(at);
		code.Bytes(0x80, 0x7E, 0x47, 0x08);                                             // cmp byte [esi+0x47], 8
		code.Jump(0x73, "original");                                                    // jae .original
		code.Bytes(0xC6, 0x46, 0x4E, 0x07);                                             // mov byte [esi+0x4E], 7
		code.Mark("original");
		code.Bytes(0x8A, 0x56, 0x4E);                                                   // mov dl, [esi+0x4E]
		code.Bytes(0xFE, 0xC2);                                                         // inc dl
		code.JumpTo(sites.Phase + 5);
		return code.Build();
	}

	/// <summary>
	/// The planner's wait (a cross asked on the run): its counter (+0x220) jumps to 7 so that the increment makes 8, the value at which the planner
	/// calls the cross start whatever the ball's place. Without it the gauge only starts 7 frames after the press, and a short press leaves it empty.
	/// </summary>
	private static byte[] PlannerWait(int at, CrossGaugeSites sites)
	{
		var code = new CodeBuilder(at);
		code.Bytes(0x80, 0x7E, 0x47, 0x08);                                             // cmp byte [esi+0x47], 8
		code.Jump(0x73, "original");                                                    // jae .original
		code.Bytes(0xC6, 0x86, 0x20, 0x02, 0x00, 0x00, 0x07);                           // mov byte [esi+0x220], 7
		code.Mark("original");
		code.Bytes(0x8A, 0x96, 0x20, 0x02, 0x00, 0x00);                                 // mov dl, [esi+0x220]
		code.JumpTo(sites.PlannerWait + 6);
		return code.Build();
	}

	/// <summary>Power: replaces what the game asked (eax) for a human.</summary>
	private static byte[] Power(int at, CrossGaugeSites sites)
	{
		var code = new CodeBuilder(at);
		code.Bytes([0x8B, 0x3D], sites.Fatigue);                                        // mov edi, [fatigue]   (replayed)
		code.Bytes(0x80, 0x7E, 0x47, 0x08);                                             // cmp byte [esi+0x47], 8
		code.Jump(0x73, "done");                                                        // jae .done
		code.Bytes(0x52);                                                               // push edx
		code.Bytes(0x0F, 0xB6, 0x56, 0x4D);                                             // movzx edx, byte [esi+0x4D]
		code.Bytes(0x81, 0xFA, 0x80, 0x00, 0x00, 0x00);                                 // cmp edx, 0x80
		code.Jump(0x76, "ok");                                                          // jbe .ok
		code.Bytes(0xBA, 0x80, 0x00, 0x00, 0x00);                                       // mov edx, 0x80
		code.Mark("ok");
		code.Bytes([0x69, 0xD2], PowerSpan);                                            // imul edx, edx, span
		code.Bytes(0xC1, 0xEA, 0x07);                                                   // shr edx, 7
		code.Bytes([0x81, 0xC2], PowerMinimum);                                         // add edx, minimum
		code.Bytes(0x89, 0xD0);                                                         // mov eax, edx
		code.Bytes(0x5A);                                                               // pop edx
		code.Mark("done");
		code.JumpTo(sites.Power + 6);
		return code.Build();
	}

	/// <summary>Cap: the stat's cap grows with the gauge.</summary>
	private static byte[] Cap(int at, CrossGaugeSites sites)
	{
		var code = new CodeBuilder(at);
		code.Bytes(0x03, 0xC2);                                                         // add eax, edx   (replayed)
		code.Bytes(0x80, 0x7E, 0x47, 0x08);                                             // cmp byte [esi+0x47], 8
		code.Jump(0x73, "done");                                                        // jae .done
		code.Bytes(0x0F, 0xB6, 0x4E, 0x4D);                                             // movzx ecx, byte [esi+0x4D]
		code.Bytes([0x69, 0xC9], CapBonus);                                             // imul ecx, ecx, bonus
		code.Bytes(0xC1, 0xE9, 0x07);                                                   // shr ecx, 7
		code.Bytes(0x01, 0xC8);                                                         // add eax, ecx
		code.Mark("done");
		code.Bytes(0x50, 0x53);                                                         // push eax ; push ebx   (replayed)
		code.Bytes(0x89, 0x44, 0x24, 0x28);                                             // mov [esp+0x28], eax
		code.JumpTo(sites.Cap + 8);
		return code.Build();
	}

	/// <summary>Half-width of the cone around the direction in which a team-mate is taken, in 1/256 of a turn (45 degrees).</summary>
	private const byte AimCone = 0x20;

	/// <summary>Distance the gauge asks for: this base + gauge x 8 + gauge^2 x 7 / 16, fitted on the falls measured in game (about 5300 at gauge 11, 13200 when full).</summary>
	private const int AimBase = 0x1400;

	/// <summary>st0 = st1 = the game's lead factor (eax used).</summary>
	private static void Lead(CodeBuilder code, CrossGaugeSites sites)
	{
		code.Bytes([0x0F, 0xB6, 0x05], sites.Lead);                                     // movzx eax, byte [lead]
		code.Bytes(0x50, 0xDB, 0x04, 0x24, 0x58);                                       // push eax ; fild dword [esp] ; pop eax
		code.Bytes(0xD9, 0xC0);                                                         // fld st0
	}

	/// <summary>
	/// The aim: for a human, replaces the receiver the game chose (al, stored in +0x48 by the replayed bytes) and the aimed point. The direction is
	/// the stick's angle when a direction is held (+0xB0 &amp; 0xF0), else the player's facing (+0x26). Among the team-mates within the cone, the one
	/// with the smallest (angle difference + 1) x |distance - distance wanted| is taken, at his place a little ahead (position + speed x lead);
	/// with nobody there the aimed point is at the distance wanted in that direction and the game's receiver stays.
	/// </summary>
	private static byte[] Aim(int at, CrossGaugeSites sites)
	{
		var code = new CodeBuilder(at);
		code.Bytes(0x8B, 0x4C, 0x24, 0x28);                                             // mov ecx, [esp+0x28]   (replayed)
		code.Bytes(0x88, 0x46, 0x48);                                                   // mov [esi+0x48], al    (replayed)
		code.Bytes(0x80, 0x7E, 0x47, 0x08);                                             // cmp byte [esi+0x47], 8
		code.Jump(0x72, "go");                                                          // jb .go   (a human)
		code.JumpTo(sites.Aim + 7);
		code.Mark("go");
		code.Bytes(0x60);                                                               // pushad
		code.Bytes(0xF6, 0x86, 0xB0, 0x00, 0x00, 0x00, 0xF0);                           // test byte [esi+0xB0], 0xF0   (a direction is held)
		code.Jump(0x74, "facing");                                                      // jz .facing
		code.Bytes(0x56);                                                               // push esi
		code.CallTo(sites.StickAngle);
		code.Bytes(0x83, 0xC4, 0x04);                                                   // add esp, 4
		code.Bytes(0x0F, 0xB7, 0xE8);                                                   // movzx ebp, ax
		code.Jump(0xEB, "have");                                                        // jmp .have
		code.Mark("facing");
		code.Bytes(0x0F, 0xB7, 0x6E, 0x26);                                             // movzx ebp, word [esi+0x26]
		code.Mark("have");
		// edi = the first player of his team (ids 1 to 11, 12 to 22: the players follow each other, 0x240 bytes each)
		code.Bytes(0x0F, 0xB6, 0x06);                                                   // movzx eax, byte [esi]
		code.Bytes(0x83, 0xF8, 0x0B);                                                   // cmp eax, 11
		code.Jump(0x76, "first");                                                       // jbe .first
		code.Bytes(0x83, 0xE8, 0x0B);                                                   // sub eax, 11
		code.Mark("first");
		code.Bytes(0x48);                                                               // dec eax
		code.Bytes(0x69, 0xC0, 0x40, 0x02, 0x00, 0x00);                                 // imul eax, eax, 0x240
		code.Bytes(0x89, 0xF7);                                                         // mov edi, esi
		code.Bytes(0x29, 0xC7);                                                         // sub edi, eax
		code.Bytes(0x6A, 0x0B, 0x6A, 0x00);                                             // push 11 ; push 0   (counter, best player)
		code.Bytes(0x68, 0xFF, 0xFF, 0xFF, 0x7F);                                       // push 0x7FFFFFFF   (best score)
		code.Bytes(0x0F, 0xB6, 0x46, 0x4D);                                             // movzx eax, byte [esi+0x4D]   (gauge)
		code.Bytes(0x89, 0xC1);                                                         // mov ecx, eax
		code.Bytes(0x0F, 0xAF, 0xC0, 0x6B, 0xC0, 0x07, 0xC1, 0xE8, 0x04);               // imul eax, eax ; imul eax, eax, 7 ; shr eax, 4
		code.Bytes([0x8D, 0x84, 0xC8], AimBase);                                        // lea eax, [eax+ecx*8+base]
		code.Bytes(0x50);                                                               // push eax   (distance wanted)
		code.Mark("loop");
		code.Bytes(0x39, 0xF7);                                                         // cmp edi, esi
		code.JumpNear(0x84, "next");                                                    // je .next   (himself)
		code.Bytes(0x57);                                                               // push edi
		code.CallTo(sites.OnPitch);
		code.Bytes(0x83, 0xC4, 0x04);                                                   // add esp, 4
		code.Bytes(0x85, 0xC0);                                                         // test eax, eax
		code.JumpNear(0x84, "next");                                                    // jz .next
		code.Bytes(0x83, 0xEC, 0x08);                                                   // sub esp, 8   (his place ahead: x, z)
		Lead(code, sites);
		code.Bytes(0xD8, 0x8F, 0xF0, 0x00, 0x00, 0x00, 0xD8, 0x87, 0xE0, 0x00, 0x00, 0x00);   // fmul [edi+0xF0] ; fadd [edi+0xE0]
		code.Bytes(0xD9, 0x1C, 0x24);                                                   // fstp dword [esp]
		code.Bytes(0xD8, 0x8F, 0xF8, 0x00, 0x00, 0x00, 0xD8, 0x87, 0xE8, 0x00, 0x00, 0x00);   // fmul [edi+0xF8] ; fadd [edi+0xE8]
		code.Bytes(0xD9, 0x5C, 0x24, 0x04);                                             // fstp dword [esp+4]
		code.Bytes(0xFF, 0x74, 0x24, 0x04, 0xFF, 0x74, 0x24, 0x04);                     // push dword [esp+4] twice   (z, then x)
		code.Bytes(0xFF, 0xB6, 0xE8, 0x00, 0x00, 0x00, 0xFF, 0xB6, 0xE0, 0x00, 0x00, 0x00);   // push dword [esi+0xE8] ; push dword [esi+0xE0]
		code.CallTo(sites.AngleBetween);
		code.Bytes(0x83, 0xC4, 0x10);                                                   // add esp, 0x10
		code.Bytes(0x29, 0xE8, 0x0F, 0xBF, 0xC0);                                       // sub eax, ebp ; movsx eax, ax
		code.Bytes(0x99, 0x31, 0xD0, 0x29, 0xD0);                                       // cdq ; xor eax, edx ; sub eax, edx   (absolute)
		code.Bytes(0xC1, 0xE8, 0x08);                                                   // shr eax, 8   (1/256 of a turn)
		code.Bytes(0x83, 0xF8, AimCone);                                                // cmp eax, cone
		code.Jump(0x77, "pop");                                                         // ja .pop
		code.Bytes(0x40, 0x89, 0xC3);                                                   // inc eax ; mov ebx, eax
		code.Bytes(0xD9, 0x04, 0x24, 0xD8, 0xA6, 0xE0, 0x00, 0x00, 0x00, 0xD8, 0xC8);   // fld [esp] ; fsub [esi+0xE0] ; fmul st0
		code.Bytes(0xD9, 0x44, 0x24, 0x04, 0xD8, 0xA6, 0xE8, 0x00, 0x00, 0x00, 0xD8, 0xC8);   // fld [esp+4] ; fsub [esi+0xE8] ; fmul st0
		code.Bytes(0xDE, 0xC1, 0xD9, 0xFA, 0xDB, 0x1C, 0x24);                           // faddp ; fsqrt ; fistp dword [esp]   (distance)
		code.Bytes(0x8B, 0x04, 0x24, 0x2B, 0x44, 0x24, 0x08);                           // mov eax, [esp] ; sub eax, [esp+8]   (wanted)
		code.Bytes(0x99, 0x31, 0xD0, 0x29, 0xD0);                                       // absolute
		code.Bytes(0x0F, 0xAF, 0xC3);                                                   // imul eax, ebx
		code.Bytes(0x3B, 0x44, 0x24, 0x0C);                                             // cmp eax, [esp+0xC]   (best score)
		code.Jump(0x73, "pop");                                                         // jae .pop
		code.Bytes(0x89, 0x44, 0x24, 0x0C, 0x89, 0x7C, 0x24, 0x10);                     // mov [esp+0xC], eax ; mov [esp+0x10], edi
		code.Mark("pop");
		code.Bytes(0x83, 0xC4, 0x08);                                                   // add esp, 8
		code.Mark("next");
		code.Bytes(0x81, 0xC7, 0x40, 0x02, 0x00, 0x00);                                 // add edi, 0x240
		code.Bytes(0xFF, 0x4C, 0x24, 0x0C);                                             // dec dword [esp+0xC]
		code.JumpNear(0x85, "loop");                                                    // jnz .loop
		code.Bytes(0x59, 0x58, 0x5F, 0x58);                                             // pop ecx (wanted) ; pop eax ; pop edi (best player) ; pop eax
		code.Bytes(0x85, 0xFF);                                                         // test edi, edi
		code.Jump(0x74, "free");                                                        // jz .free
		code.Bytes(0x8A, 0x07, 0x88, 0x46, 0x48);                                       // mov al, [edi] ; mov [esi+0x48], al
		Lead(code, sites);
		code.Bytes(0xD8, 0x8F, 0xF0, 0x00, 0x00, 0x00, 0xD8, 0x87, 0xE0, 0x00, 0x00, 0x00);
		code.Bytes(0xDB, 0x9E, 0x9C, 0x00, 0x00, 0x00);                                 // fistp dword [esi+0x9C]
		code.Bytes(0xD8, 0x8F, 0xF8, 0x00, 0x00, 0x00, 0xD8, 0x87, 0xE8, 0x00, 0x00, 0x00);
		code.Bytes(0xDB, 0x9E, 0xA0, 0x00, 0x00, 0x00);                                 // fistp dword [esi+0xA0]
		code.Jump(0xEB, "end");                                                         // jmp .end
		code.Mark("free");
		code.Bytes(0x55, 0xDB, 0x04, 0x24);                                             // push ebp ; fild dword [esp]   (angle)
		code.AddressOf([0xD8, 0x0D], "turn");                                           // fmul dword [turn]
		code.Bytes(0xD9, 0xFB);                                                         // fsincos   (st0 = cos, st1 = sin)
		code.Bytes(0x89, 0x0C, 0x24, 0xDB, 0x04, 0x24);                                 // mov [esp], ecx ; fild dword [esp]   (wanted)
		code.Bytes(0xDC, 0xC9, 0xDE, 0xCA);                                             // fmul st1, st0 ; fmulp st2, st0
		code.Bytes(0xD8, 0x86, 0xE0, 0x00, 0x00, 0x00, 0xDB, 0x9E, 0x9C, 0x00, 0x00, 0x00);   // x + cos x wanted
		code.Bytes(0xD8, 0xAE, 0xE8, 0x00, 0x00, 0x00, 0xDB, 0x9E, 0xA0, 0x00, 0x00, 0x00);   // z - sin x wanted
		code.Bytes(0x83, 0xC4, 0x04);                                                   // add esp, 4
		code.Mark("end");
		code.Bytes(0x61);                                                               // popad
		code.JumpTo(sites.Aim + 7);
		code.Mark("turn");
		code.Bytes(BitConverter.GetBytes((float)(2 * Math.PI / 65536)));                // an angle unit in radians
		return code.Build();
	}

	/// <summary>
	/// Adds to <paramref name="plan"/> the caves (placed by <paramref name="cave"/>, which takes a size), the hooks that jump to them and
	/// the fill time. The order of the allocations is fixed.
	/// </summary>
	internal static void Plan(CrossGaugeSites sites, int fillMs, List<(int Va, byte[] Bytes)> plan, Func<int, int> cave)
	{
		if (fillMs is < PatchOptions.MinCrossGaugeMs or > PatchOptions.MaxCrossGaugeMs)
			throw new ArgumentOutOfRangeException(nameof(fillMs), fillMs, null);

		int Place(Func<int, byte[]> build)
		{
			int at = cave(build(0).Length);
			plan.Add((at, build(at)));
			return at;
		}

		int helper = Place(at => Helper(at, sites));
		var hooks = new List<(int Site, int Length, int Cave)>
		{
			(sites.Call1, 5, Place(at => Relay(at, helper, sites.Call1 + 5, sites.StartCross, [0xC6, 0x46, 0x4E, 0x07]))),
			(sites.Call2, 5, Place(at => Relay(at, helper, sites.Call2 + 5, sites.StartCross, [0xC6, 0x46, 0x4E, 0x07]))),
			// the planner's wait counter is +0x220, not +0x4E
			(sites.Call3, 5, Place(at => Relay(at, helper, sites.Call3 + 5, sites.StartCross, [0xC6, 0x86, 0x20, 0x02, 0x00, 0x00, 0x07]))),
			(sites.Phase, 5, Place(at => Phase(at, sites))),
			(sites.Power, 6, Place(at => Power(at, sites))),
			(sites.Cap, 8, Place(at => Cap(at, sites))),
			(sites.Aim, 7, Place(at => Aim(at, sites))),
			(sites.PlannerWait, 6, Place(at => PlannerWait(at, sites))),
		};
		foreach (var (site, length, at) in hooks) plan.Add((site, CodeBuilder.JumpPatch(site, at, length)));
		plan.Add((sites.State, BitConverter.GetBytes(fillMs)));
	}
}
