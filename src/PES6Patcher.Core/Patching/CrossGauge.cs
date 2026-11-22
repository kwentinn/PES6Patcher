namespace PES6Patcher.Core.Patching;

/// <summary>
/// Cross gauge: a human player's cross (player state 9, circle from a wing) is held while the circle stays down and its power follows
/// how long it was held, instead of being the game's automatic one. The gauge (the player's byte +0x4D, 0 to 0x80) fills in
/// <see cref="PatchOptions.CrossGaugeMs"/> milliseconds on the game's own clock (GetTickCount); the cross starts when the circle is let go,
/// or at once when the gauge is full.
/// </summary>
/// <remarks>
/// The cross is started by one function called from three places. Each call goes through a relay: for a human (pad number below 8) whose
/// circle (bit 0x2000 of +0xB0) is held and whose gauge is not full, the call is skipped, the gauge is updated and the game's wait counter
/// is kept short so that it asks again at the next frame; otherwise the call is made. Another hook at the start of the wait phase sets that
/// counter so that a cross does not wait the game's 7 frames. The power the game asks is replaced by one that follows the gauge, and the
/// stat's cap is lifted by the gauge, up to the kick function's own ceiling (0x9000).
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
		};
		foreach (var (site, length, at) in hooks) plan.Add((site, CodeBuilder.JumpPatch(site, at, length)));
		plan.Add((sites.State, BitConverter.GetBytes(fillMs)));
	}
}
