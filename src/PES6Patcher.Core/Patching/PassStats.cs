namespace PES6Patcher.Core.Patching;

/// <summary>
/// Passes by the stats, and the lofted through ball (L1 + triangle), issue #67. Both live in the game's ground pass code, which also kicks
/// the through balls: its pass kind (+0x4C) is 3 or 7 for L1 + triangle.
/// </summary>
/// <remarks>
/// <para>Pass accuracy: the direction error of a ground pass is one signed draw (<see cref="PassStatsSites.Random"/>, uniform in [-range, +range]).
/// Its range barely follows the passer: about 3 to 5 degrees whatever his stat (measured in CPU matches). The range is multiplied by
/// (d / n)^3, d = 99 - accuracy (0 to 45), n = 99 - the neutral accuracy (setting, 80 by default: 0.1 at 90, 3.6 at 70, 8.7 at 60). The accuracy is the short pass one, the long pass
/// one for L1 + triangle. L1 + triangle also gets a distance error: its speed is drawn within +-60 (d / n)^2 per mille (at most 250).</para>
/// <para>Lofted through ball: its aimed point stays about 12 m before the goal line and 6 m inside the touchline (the game put it on the goal
/// line, or out, and the keeper or the touchline took the ball); its speed and height are settings (70 % and 115 % of the game's by default: it
/// landed 1.2 to 2.3 times as far as its point), and so is its backspin (the ball's +0x74, which brakes it at the bounce and also lifts it in
/// flight: 23 % of its speed by default, the game giving about 17 %). The defaults were tuned with the user in training and matches, 2026-11-25.</para>
/// </remarks>
internal static class PassStats
{
	/// <summary>Lofted through ball's aimed point: most |x| (the goal lines are at about 12 400) and most |z| (the touchlines at about 8 000).</summary>
	private const int GoalBound = 9600, TouchBound = 6600;

	/// <summary>d = 99 - accuracy is clamped to this; the factor is (d / n)^3, n = 99 - the neutral accuracy (19 by default: (d / 19)^3 = d^3 / 6859).</summary>
	private const int MaxGap = 45;

	/// <summary>Most of the direction range, in the angle's units (0x10000 a turn): 33.75 degrees.</summary>
	private const int MaxRange = 0x1800;

	/// <summary>Lofted through ball's distance error: range = 60 (d / n)^2 per mille, at most 250.</summary>
	private const int DistanceScale = 60, MaxDistance = 250;

	/// <summary>A share in % as a factor in 1/128 (70 % gives 90, 115 % gives 147, 23 % gives 29).</summary>
	private static int Per128(int percent) => (int)Math.Round(percent * 128 / 100.0);

	/// <summary>The pass kinds of L1 + triangle (3 and 7: kind &amp; 0xFB = 3).</summary>
	private const byte LoftedKind = 3, LoftedKindMask = 0xFB, LoftedKindOther = 7;

	/// <summary>Player offsets: team, slot, pass kind, aimed point x and z. Ball offsets: speed, spin. Stats: short and long pass accuracy.</summary>
	private const byte Team = 0x12, Slot = 0x11, Kind = 0x4C, BallSpeed = 0x50, BallSpin = 0x74;
	private const int PointX = 0x9C, PointZ = 0xA0, ShortPass = 0xC, StatsSize = 0x348;

	/// <summary>
	/// Emits: eax = d = 99 - the accuracy of the player in <paramref name="player"/>'s register (esi: 0x46, edi: 0x47 in the ModR/M byte), clamped
	/// to 0..45. Uses ecx. The accuracy is the long pass one for L1 + triangle when <paramref name="byKind"/>, else <paramref name="stat"/>.
	/// </summary>
	private static void Gap(CodeBuilder code, PassStatsSites sites, byte player, int stat, bool byKind)
	{
		code.Bytes(0x0F, 0xB6, player, Team);                                           // movzx eax, byte [player+0x12]
		code.Bytes(0x0F, 0xB6, (byte)(player + 8), Slot);                               // movzx ecx, byte [player+0x11]
		code.Bytes(0xC1, 0xE0, 0x05);                                                   // shl eax, 5
		code.Bytes(0x01, 0xC8);                                                         // add eax, ecx
		code.Bytes([0x69, 0xC0], StatsSize);                                            // imul eax, eax, 0x348
		if (byKind)
		{
			code.Bytes(0x8A, (byte)(player + 8), Kind);                                 // mov cl, [player+0x4C]
			code.Bytes(0x80, 0xE1, LoftedKindMask);                                     // and cl, 0xFB
			code.Bytes(0x80, 0xF9, LoftedKind);                                         // cmp cl, 3
			code.Jump(0x75, "gap.stat");                                                // jne .stat
			code.Bytes(0x83, 0xC0, 0x02);                                               // add eax, 2   (long pass accuracy: +0xE)
			code.Mark("gap.stat");
		}
		code.Bytes([0x0F, 0xB6, 0x88], sites.Stats + stat);                             // movzx ecx, byte [eax+stats+stat]
		code.Bytes(0x83, 0xE1, 0x7F);                                                   // and ecx, 0x7F
		code.Bytes(0xB8, 0x63, 0x00, 0x00, 0x00);                                       // mov eax, 99
		code.Bytes(0x29, 0xC8);                                                         // sub eax, ecx
		code.Jump(0x79, "gap.positive");                                                // jns .positive
		code.Bytes(0x31, 0xC0);                                                         // xor eax, eax
		code.Mark("gap.positive");
		code.Bytes(0x83, 0xF8, MaxGap);                                                 // cmp eax, 45
		code.Jump(0x7E, "gap.done");                                                    // jle .done
		code.Bytes([0xB8], MaxGap);                                                     // mov eax, 45
		code.Mark("gap.done");
	}

	/// <summary>Called instead of the signed draw (esi = the passer, the range at [esp+4]): scales the range, then goes on to the draw.</summary>
	private static byte[] Direction(int at, PassStatsSites sites, int cube)
	{
		var code = new CodeBuilder(at);
		Gap(code, sites, 0x46, ShortPass, byKind: true);
		code.Bytes(0x89, 0xC1);                                                         // mov ecx, eax
		code.Bytes(0x0F, 0xAF, 0xC0);                                                   // imul eax, eax
		code.Bytes(0x0F, 0xAF, 0xC1);                                                   // imul eax, ecx   (d^3)
		code.Bytes(0x0F, 0xAF, 0x44, 0x24, 0x04);                                       // imul eax, [esp+4]
		code.Bytes([0xB9], cube);                                                       // mov ecx, n^3   (6859 at 80)
		code.Bytes(0x99);                                                               // cdq
		code.Bytes(0xF7, 0xF9);                                                         // idiv ecx
		code.Bytes([0x3D], MaxRange);                                                   // cmp eax, 0x1800
		code.Jump(0x7E, "store");                                                       // jle .store
		code.Bytes([0xB8], MaxRange);                                                   // mov eax, 0x1800
		code.Mark("store");
		code.Bytes(0x89, 0x44, 0x24, 0x04);                                             // mov [esp+4], eax
		code.JumpTo(sites.Random);
		return code.Build();
	}

	/// <summary>After the through ball's point is set (edi = the passer, ebp = the start's x): bounds the point of L1 + triangle.</summary>
	private static byte[] Point(int at, PassStatsSites sites, byte[] original)
	{
		var code = new CodeBuilder(at);
		code.Bytes(original[3..14]);                                                    // add esp,0x10 ; cmp al,3 ; mov [edi+0xA0],edx   (replayed, flags rebuilt below)
		code.Bytes(0x50, 0x51, 0x52);                                                   // push eax ; push ecx ; push edx
		code.Bytes(0x0F, 0xB6, 0x47, Kind);                                             // movzx eax, byte [edi+0x4C]
		code.Bytes(0x3C, LoftedKind);                                                   // cmp al, 3
		code.Jump(0x74, "lofted");                                                      // je .lofted
		code.Bytes(0x3C, LoftedKindOther);                                              // cmp al, 7
		code.JumpNear(0x85, "done");                                                    // jne .done
		code.Mark("lofted");
		// |x| of the point: at most the bound, or the start's |x| on that side when the start is already past it, never beyond the game's point
		code.Bytes([0x8B, 0x87], PointX);                                               // mov eax, [edi+0x9C]
		code.Bytes(0x99, 0x31, 0xD0, 0x29, 0xD0);                                       // cdq ; xor eax,edx ; sub eax,edx   (|x|, edx = its sign)
		code.Bytes([0x3D], GoalBound);                                                  // cmp eax, bound
		code.Jump(0x7E, "touch");                                                       // jle .touch
		code.Bytes(0x89, 0xE9, 0x31, 0xD1, 0x29, 0xD1);                                 // mov ecx,ebp ; xor ecx,edx ; sub ecx,edx   (the start's x towards that side)
		code.Bytes([0x81, 0xF9], GoalBound);                                            // cmp ecx, bound
		code.Jump(0x7D, "start");                                                       // jge .start
		code.Bytes([0xB9], GoalBound);                                                  // mov ecx, bound
		code.Mark("start");
		code.Bytes(0x39, 0xC1);                                                         // cmp ecx, eax
		code.Jump(0x7E, "x");                                                           // jle .x
		code.Bytes(0x89, 0xC1);                                                         // mov ecx, eax
		code.Mark("x");
		code.Bytes(0x31, 0xD1, 0x29, 0xD1);                                             // xor ecx,edx ; sub ecx,edx
		code.Bytes([0x89, 0x8F], PointX);                                               // mov [edi+0x9C], ecx
		code.Mark("touch");
		code.Bytes([0x8B, 0x87], PointZ);                                               // mov eax, [edi+0xA0]
		code.Bytes(0x99, 0x31, 0xD0, 0x29, 0xD0);                                       // |z|
		code.Bytes([0x3D], TouchBound);                                                 // cmp eax, bound
		code.Jump(0x7E, "done");                                                        // jle .done
		code.Bytes([0xB8], TouchBound);                                                 // mov eax, bound
		code.Bytes(0x31, 0xD0, 0x29, 0xD0);                                             // xor eax,edx ; sub eax,edx
		code.Bytes([0x89, 0x87], PointZ);                                               // mov [edi+0xA0], eax
		code.Mark("done");
		code.Bytes(0x5A, 0x59, 0x58);                                                   // pop edx ; pop ecx ; pop eax
		code.Bytes(original[0..3]);                                                     // mov al, [edi+0x4C]   (replayed)
		code.Bytes(0x3C, LoftedKind);                                                   // cmp al, 3   (the flags for the game's je)
		code.JumpTo(sites.Point + original.Length);
		return code.Build();
	}

	/// <summary>
	/// Once the pass's final speed (edi) and height (ebp) are set (esi = the passer): for L1 + triangle, the lofted through ball's speed and height
	/// (<paramref name="lofted"/>) and its distance error (<paramref name="accuracy"/>); then the game's push and call.
	/// </summary>
	private static byte[] Speed(int at, PassStatsSites sites, (int Speed, int Height)? lofted, int square)
	{
		bool accuracy = square > 0;
		var code = new CodeBuilder(at);
		code.Bytes(0x80, 0x7E, Kind, LoftedKind);                                       // cmp byte [esi+0x4C], 3
		code.Jump(0x74, "lofted");                                                      // je .lofted
		code.Bytes(0x80, 0x7E, Kind, LoftedKindOther);                                  // cmp byte [esi+0x4C], 7
		code.JumpNear(0x85, "done");                                                    // jne .done
		code.Mark("lofted");
		if (lofted is var (speed, height))
		{
			code.Bytes([0x69, 0xFF], speed);                                            // imul edi, edi, speed   (90 by default)
			code.Bytes(0xC1, 0xFF, 0x07);                                               // sar edi, 7
			code.Bytes([0x69, 0xED], height);                                           // imul ebp, ebp, height   (147 by default)
			code.Bytes(0xC1, 0xFD, 0x07);                                               // sar ebp, 7
		}
		if (accuracy)
		{
			code.Bytes(0x50, 0x51, 0x52);                                               // push eax ; push ecx ; push edx
			Gap(code, sites, 0x46, ShortPass + 2, byKind: false);                       // d from the long pass accuracy
			code.Bytes(0x0F, 0xAF, 0xC0);                                               // imul eax, eax
			code.Bytes(0x6B, 0xC0, DistanceScale);                                      // imul eax, eax, 60
			code.Bytes([0xB9], square);                                                 // mov ecx, n^2   (361 at 80)
			code.Bytes(0x99, 0xF7, 0xF9);                                               // cdq ; idiv ecx
			code.Bytes([0x3D], MaxDistance);                                            // cmp eax, 250
			code.Jump(0x7E, "range");                                                   // jle .range
			code.Bytes([0xB8], MaxDistance);                                            // mov eax, 250
			code.Mark("range");
			code.Bytes(0x50);                                                           // push eax
			code.CallTo(sites.Random);                                                  // r in [-range, +range]
			code.Bytes(0x83, 0xC4, 0x04);                                               // add esp, 4
			code.Bytes([0x05], 1000);                                                   // add eax, 1000
			code.Bytes(0x89, 0xC1, 0x89, 0xF8);                                         // mov ecx,eax ; mov eax,edi
			code.Bytes(0xF7, 0xE9);                                                     // imul ecx
			code.Bytes([0xB9], 1000);                                                   // mov ecx, 1000
			code.Bytes(0xF7, 0xF9, 0x89, 0xC7);                                         // idiv ecx ; mov edi, eax
			code.Bytes(0x5A, 0x59, 0x58);                                               // pop edx ; pop ecx ; pop eax
		}
		code.Mark("done");
		code.Bytes(0x51);                                                               // push ecx   (replayed)
		code.CallTo(sites.SpeedCall);                                                   // (replayed)
		code.JumpTo(sites.Speed + 6);
		return code.Build();
	}

	/// <summary>Where the pass kinds meet again after the kick (esi = the passer): the backspin of L1 + triangle.</summary>
	private static byte[] Spin(int at, PassStatsSites sites, byte[] original, int spin)
	{
		var code = new CodeBuilder(at);
		code.Bytes(original);                                                           // push 3 ; mov byte [..],0   (replayed)
		code.Bytes(0x80, 0x7E, Kind, LoftedKind);                                       // cmp byte [esi+0x4C], 3
		code.Jump(0x74, "lofted");                                                      // je .lofted
		code.Bytes(0x80, 0x7E, Kind, LoftedKindOther);                                  // cmp byte [esi+0x4C], 7
		code.Jump(0x75, "done");                                                        // jne .done
		code.Mark("lofted");
		code.Bytes(0x50, 0x51);                                                         // push eax ; push ecx
		code.Bytes([0xA1], sites.Ball);                                                 // mov eax, [ball pointer]
		code.Bytes(0x8B, 0x48, BallSpeed);                                              // mov ecx, [eax+0x50]
		code.Bytes(0x6B, 0xC9, unchecked((byte)(sbyte)-spin));                          // imul ecx, ecx, -spin   (-29 by default)
		code.Bytes(0xC1, 0xF9, 0x07);                                                   // sar ecx, 7
		code.Bytes(0x89, 0x48, BallSpin);                                               // mov [eax+0x74], ecx
		code.Bytes(0x59, 0x58);                                                         // pop ecx ; pop eax
		code.Mark("done");
		code.JumpTo(sites.Spin + original.Length);
		return code.Build();
	}

	/// <summary>Adds to <paramref name="plan"/> the caves (placed by <paramref name="cave"/>, which takes a size) and the hooks that reach them.</summary>
	internal static void Plan(PeImage image, PassStatsSites sites, PatchOptions options, List<(int Va, byte[] Bytes)> plan, Func<int, int> cave)
	{
		bool accuracy = options.PassAccuracy, lofted = options.LoftedThrough;
		if (accuracy && options.PassAccuracyNeutral is < PatchOptions.MinPassAccuracyNeutral or > PatchOptions.MaxPassAccuracyNeutral)
			throw new ArgumentOutOfRangeException(nameof(options), options.PassAccuracyNeutral, null);
		if (lofted && (options.LoftedSpeed is < PatchOptions.MinLoftedSpeed or > PatchOptions.MaxLoftedSpeed
			|| options.LoftedHeight is < PatchOptions.MinLoftedHeight or > PatchOptions.MaxLoftedHeight
			|| options.LoftedSpin is < PatchOptions.MinLoftedSpin or > PatchOptions.MaxLoftedSpin))
			throw new ArgumentOutOfRangeException(nameof(options));
		int n = 99 - options.PassAccuracyNeutral;

		int Place(Func<int, byte[]> build)
		{
			int at = cave(build(0).Length);
			plan.Add((at, build(at)));
			return at;
		}

		byte[] Read(int va, int length) => image.Bytes[image.Offset(va)..(image.Offset(va) + length)];

		if (accuracy)
		{
			int direction = Place(at => Direction(at, sites, n * n * n));
			plan.Add((sites.DirectionMain, CodeBuilder.CallPatch(sites.DirectionMain, direction, 5)));
			plan.Add((sites.DirectionVariant, CodeBuilder.CallPatch(sites.DirectionVariant, direction, 5)));
		}
		if (lofted)
		{
			var point = Read(sites.Point, 14);
			plan.Add((sites.Point, CodeBuilder.JumpPatch(sites.Point, Place(at => Point(at, sites, point)), point.Length)));
		}
		(int, int)? scales = lofted ? (Per128(options.LoftedSpeed), Per128(options.LoftedHeight)) : null;
		plan.Add((sites.Speed, CodeBuilder.JumpPatch(sites.Speed, Place(at => Speed(at, sites, scales, accuracy ? n * n : 0)), 6)));
		if (lofted)
		{
			var spin = Read(sites.Spin, 9);
			plan.Add((sites.Spin, CodeBuilder.JumpPatch(sites.Spin, Place(at => Spin(at, sites, spin, Per128(options.LoftedSpin))), spin.Length)));
		}
	}
}
