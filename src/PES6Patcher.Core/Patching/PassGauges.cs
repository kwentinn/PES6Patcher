namespace PES6Patcher.Core.Patching;

internal static class PassGauges
{
	internal sealed record Payload(byte[] Bytes, int[] Entries, int FrameSettings);

	internal static Payload Build(int origin, PassGaugeSites sites, PatchOptions options, int leadState, int kickNext = 0)
	{
		if (new[] { options.ShortPassFrames, options.ThroughPassFrames, options.LoftedThroughFrames }.Any(frames => frames is < 4 or > 60))
			throw new ArgumentOutOfRangeException(nameof(options), Loc.T("Error.PassGaugeFrames"));
		if (options.PassLead is < PatchOptions.MinPassLead or > PatchOptions.MaxPassLead)
			throw new ArgumentOutOfRangeException(nameof(options), options.PassLead, null);

		var code = new CodeBuilder(origin);
		code.Mark("dispatchEntry");
		code.JumpTo("dispatch"); // jmp near dispatch
		code.Mark("kickEntry");
		code.JumpTo("kick"); // jmp near kick
		code.Mark("leadXEntry");
		code.JumpTo("lead_x"); // jmp near lead_x
		code.Mark("leadZEntry");
		code.JumpTo("lead_z"); // jmp near lead_z
		code.Mark("hudEntry");
		code.JumpTo("hud");
		code.Mark("dispatch");
		code.Bytes(0x9C); // pushfd
		code.Bytes(0x60); // pushad
		if (options.ShotGaugeSpeed < 100) CodeCaves.ShotGaugeCore(code, options.ShotGaugeSpeed); // slowed shot gauge, see ShotGaugeCore
		// Player record zero can also report pad zero. It cannot receive a pass.
		code.Bytes(0x0F, 0xB6, 0x06); // movzx eax, byte [esi]
		code.Bytes(0x48); // dec eax
		code.Bytes(0x83, 0xF8, 0x15); // cmp eax, 21
		code.JumpNear(0x87, "dispatch.done"); // ja .done
		code.Bytes(0x0F, 0xB6, 0x4E, 0x47); // movzx ecx, byte [esi+0x47]
		code.Bytes(0x83, 0xF9, 0x08); // cmp ecx, 8
		code.JumpNear(0x83, "dispatch.done"); // jae .done
		code.Bytes(0x89, 0xCB); // mov ebx, ecx
		code.Bytes(0xC1, 0xE3, 0x05); // shl ebx, 5
		code.AddressOf([0x81, 0xC3], "slots"); // add ebx, slots
		code.Bytes([0x80, 0x3D], sites.Restart).Bytes(0x00); // cmp byte [restart_type], 0
		code.JumpNear(0x85, "dispatch.clear"); // jne .clear
		code.Bytes([0x66, 0x83, 0x3D], sites.Blocked).Bytes(0x00); // cmp word [blocked], 0
		code.JumpNear(0x85, "dispatch.clear"); // jne .clear
		code.Bytes([0xA0], sites.Carrier); // mov al, [carrier]
		code.Bytes(0x3C, 0x16); // cmp al, 22
		code.Jump(0x77, "dispatch.eligible"); // ja .eligible
		code.Bytes(0x84, 0xC0); // test al, al
		code.Jump(0x74, "dispatch.eligible"); // jz .eligible
		code.Bytes(0x3A, 0x06); // cmp al, [esi]
		code.Jump(0x74, "dispatch.eligible"); // je .eligible
		// A charge must not survive a loss of possession by its owner.
		code.Bytes(0x39, 0x33); // cmp [ebx], esi
		code.JumpNear(0x85, "dispatch.done"); // jne .done
		code.Bytes(0x80, 0x7B, 0x14, 0x01); // cmp byte [ebx+20], 1
		code.JumpNear(0x84, "dispatch.clear"); // je .clear
		code.Bytes(0x80, 0x7B, 0x14, 0x03); // cmp byte [ebx+20], 3
		code.JumpNear(0x84, "dispatch.age"); // je .age
		code.JumpTo("dispatch.done");
		code.Mark("dispatch.eligible");
		code.Bytes(0x8D, 0x0C, 0x49); // lea ecx, [ecx+ecx*2]
		code.Bytes([0x8B, 0x3C, 0xCD], sites.Held); // mov edi, [ecx*8+padHeld]
		// A receiver can acquire the pad after the engine copies its pressed bits.
		// Read the same physical edge directly, limited to buttons still held.
		code.Bytes([0x8B, 0x2C, 0xCD], sites.Held - 8); // mov ebp, [ecx*8+padPressed]
		code.Bytes(0x21, 0xFD); // and ebp, edi
		code.Bytes(0x0B, 0xAE, 0xB4, 0x00, 0x00, 0x00); // or ebp, [esi+pressed]
		code.Bytes(0x8B, 0x96, 0xB0, 0x00, 0x00, 0x00); // mov edx, [esi+held]
		code.Bytes(0xF7, 0xC2, 0x00, 0xA8, 0x00, 0x00); // test edx, 0xA800
		code.JumpNear(0x85, "dispatch.clear"); // jnz .clear
		code.Bytes(0x0F, 0xB6, 0x43, 0x14); // movzx eax, byte [ebx+20]
		code.Bytes(0x83, 0xF8, 0x03); // cmp eax, 3
		code.JumpNear(0x84, "dispatch.committed"); // je .committed
		code.Bytes(0x83, 0xF8, 0x01); // cmp eax, 1
		code.Jump(0x74, "dispatch.charging"); // je .charging
		code.Mark("dispatch.read_press");
		code.Bytes(0x89, 0xE8); // mov eax, ebp
		code.Bytes(0x25, 0x00, 0x50, 0x00, 0x00); // and eax, x_button | triangle_button
		code.Bytes(0x3D, 0x00, 0x40, 0x00, 0x00); // cmp eax, x_button
		code.Jump(0x74, "dispatch.start"); // je .start
		code.Bytes(0x3D, 0x00, 0x10, 0x00, 0x00); // cmp eax, triangle_button
		code.JumpNear(0x85, "dispatch.done"); // jne .done
		code.Bytes(0x89, 0xD1); // mov ecx, edx
		code.Bytes(0x81, 0xE1, 0x00, 0x04, 0x00, 0x00); // and ecx, l1
		code.Bytes(0x09, 0xC8); // or eax, ecx
		code.Mark("dispatch.start");
		code.Bytes(0x89, 0x33); // mov [ebx], esi
		code.Bytes(0x89, 0x43, 0x04); // mov [ebx+4], eax
		code.Bytes(0xC7, 0x43, 0x08, 0x03, 0x00, 0x00, 0x00); // mov dword [ebx+8], 3
		code.Bytes(0xC7, 0x43, 0x10, 0x00, 0x00, 0x00, 0x00); // mov dword [ebx+16], 0
		code.Bytes(0xC6, 0x43, 0x14, 0x01); // mov byte [ebx+20], 1
		code.Mark("dispatch.charging");
		code.Bytes(0x8B, 0x4B, 0x04); // mov ecx, [ebx+4]
		code.Bytes(0x81, 0xE1, 0x00, 0x50, 0x00, 0x00); // and ecx, x_button | triangle_button
		code.Bytes(0x85, 0xCF); // test edi, ecx
		code.Jump(0x74, "dispatch.release"); // jz .release
		code.Bytes(0x81, 0xA6, 0xB0, 0x00, 0x00, 0x00, 0xFF, 0xAF, 0x5A, 0xF3); // and dword [esi+held], ~pass_bits
		code.Bytes(0x81, 0xA6, 0xB4, 0x00, 0x00, 0x00, 0xFF, 0xAF, 0x5A, 0xF3); // and dword [esi+pressed], ~pass_bits
		code.Bytes(0x81, 0x7B, 0x08, 0x80, 0x00, 0x00, 0x00); // cmp dword [ebx+8], 128
		code.JumpNear(0x83, "dispatch.done"); // jae .done
		code.Bytes(0xFF, 0x43, 0x10); // inc dword [ebx+16]
		code.AddressOf([0x8B, 0x0D], "shortFrames"); // mov ecx, [charge_frames]
		code.Bytes(0xF7, 0x43, 0x04, 0x00, 0x10, 0x00, 0x00); // test dword [ebx+4], triangle_button
		code.Jump(0x74, "dispatch.pass_frames"); // jz .pass_frames
		code.AddressOf([0x8B, 0x0D], "throughFrames"); // mov ecx, [charge_frames+4]
		code.Bytes(0xF7, 0x43, 0x04, 0x00, 0x04, 0x00, 0x00); // test dword [ebx+4], l1
		code.Jump(0x74, "dispatch.pass_frames"); // jz .pass_frames
		code.AddressOf([0x8B, 0x0D], "loftedFrames"); // mov ecx, [charge_frames+8]
		code.Mark("dispatch.pass_frames");
		code.Bytes(0x8B, 0x43, 0x10); // mov eax, [ebx+16]
		code.Bytes(0xC1, 0xE0, 0x07); // shl eax, 7
		code.Bytes(0x31, 0xD2); // xor edx, edx
		code.Bytes(0xF7, 0xF1); // div ecx
		code.Bytes(0x89, 0x43, 0x08); // mov [ebx+8], eax
		code.JumpTo("dispatch.done"); // jmp .done
		code.Mark("dispatch.release");
		// Keep a visible minimum for taps, with stock kick speed at this charge.
		code.Bytes(0x83, 0x7B, 0x08, 0x08); // cmp dword [ebx+8], 8
		code.Jump(0x73, "dispatch.send_release"); // jae .send_release
		code.Bytes(0xC7, 0x43, 0x08, 0x08, 0x00, 0x00, 0x00); // mov dword [ebx+8], 8
		code.Mark("dispatch.send_release");
		code.Bytes(0x89, 0x33); // mov [ebx], esi
		code.Bytes(0xC7, 0x43, 0x0C, 0x3C, 0x00, 0x00, 0x00); // mov dword [ebx+12], 60
		code.Bytes(0xC6, 0x43, 0x14, 0x03); // mov byte [ebx+20], 3
		code.Bytes(0xC6, 0x43, 0x18, 0x00); // mov byte [ebx+24], 0
		code.CallTo("dispatch.submit");
		code.JumpTo("dispatch.done");

		code.Mark("dispatch.submit");
		code.Bytes(0x8B, 0x43, 0x04); // mov eax, [ebx+4]
		code.Bytes(0xA9, 0x00, 0x40, 0x00, 0x00); // test eax, x_button
		code.Jump(0x74, "dispatch.through"); // jz .through
		code.Bytes(0xB8, 0x00, 0x40, 0x24, 0x04); // mov eax, 0x4244000
		code.JumpTo("dispatch.send"); // jmp .send
		code.Mark("dispatch.through");
		code.Bytes([0x81, 0xA6, 0xB0, 0, 0, 0], ~0x400); // and dword [esi+held], ~l1
		code.Bytes([0x81, 0xA6, 0xB4, 0, 0, 0], ~0x400); // and dword [esi+pressed], ~l1
		code.Bytes(0x25, 0x00, 0x04, 0x00, 0x00); // and eax, l1
		code.Bytes(0x0D, 0x00, 0x10, 0x81, 0x08); // or eax, 0x8811000
		code.Mark("dispatch.send");
		code.Bytes([0x81, 0xA6, 0xB0, 0, 0, 0], unchecked((int)0xF35AAFFF)); // and dword [esi+held], ~pass_bits
		code.Bytes([0x81, 0xA6, 0xB4, 0, 0, 0], unchecked((int)0xF35AAFFF)); // and dword [esi+pressed], ~pass_bits
		code.Bytes(0x09, 0x86, 0xB0, 0x00, 0x00, 0x00); // or [esi+held], eax
		code.Bytes(0x09, 0x86, 0xB4, 0x00, 0x00, 0x00); // or [esi+pressed], eax
		code.Bytes(0xC3); // ret

		code.Mark("dispatch.committed");
		// A new physical press can start charging before the previous kick has completed.
		code.Bytes(0xF7, 0xC5, 0x00, 0x50, 0x00, 0x00); // test ebp, 0x5000
		code.JumpNear(0x85, "dispatch.read_press"); // jne .read_press
		code.Bytes(0x39, 0x33); // cmp [ebx], esi
		code.Jump(0x75, "dispatch.done"); // jne .done
		// Retry a released command while the receiver is still approaching the ball.
		// Once the engine enters a pass state, wait for the kick instead.
		code.Bytes(0x80, 0x7B, 0x18, 0x00); // cmp byte [ebx+24], 0
		code.Jump(0x75, "dispatch.age"); // jne .age
		code.Bytes(0x8A, 0x46, 0x16); // mov al, [esi+0x16]
		code.Bytes(0x2C, 0x05); // sub al, 5
		code.Bytes(0x3C, 0x01); // cmp al, 1
		code.Jump(0x76, "dispatch.accepted"); // jbe .accepted
		code.Bytes(0xF6, 0x43, 0x0C, 0x03); // test byte [ebx+12], 3
		code.Jump(0x75, "dispatch.age"); // jne .age
		code.Bytes(0x83, 0x7B, 0x0C, 0x3C); // cmp dword [ebx+12], 60
		code.Jump(0x74, "dispatch.age"); // je .age
		code.CallTo("dispatch.submit");
		code.Bytes(0xFF, 0x4B, 0x0C); // dec dword [ebx+12]
		code.JumpTo("dispatch.done");
		code.Mark("dispatch.accepted");
		code.Bytes(0xC6, 0x43, 0x18, 0x01); // mov byte [ebx+24], 1
		code.Mark("dispatch.age");
		code.Bytes(0xFF, 0x4B, 0x0C); // dec dword [ebx+12]
		code.Jump(0x75, "dispatch.done"); // jnz .done
		code.Mark("dispatch.clear");
		code.Bytes(0xC6, 0x43, 0x14, 0x00); // mov byte [ebx+20], 0
		code.Mark("dispatch.done");
		code.Bytes(0x61); // popad
		code.Bytes(0x9D); // popfd
		code.Bytes(0x0F, 0xB6, 0x46, 0x16); // movzx eax, byte [esi+0x16]
		code.JumpTo(sites.DispatchContinuation); // Resume native state dispatch.
		// Override the team's final draw argument after the native widget can clear its queued fill.
		code.Mark("hud");
		code.Bytes(0x9C, 0x60); // pushfd; pushad
		code.Bytes([0x80, 0x3D], sites.Restart).Bytes(0x00); // cmp byte [restart_type], 0
		code.Jump(0x75, "hud.done"); // jne .done
		code.AddressOf([0xBB], "slots"); // mov ebx, slots
		code.Bytes(0xB9, 0x08, 0x00, 0x00, 0x00); // mov ecx, 8
		code.Mark("hud.scan");
		code.Bytes(0x80, 0x7B, 0x14, 0x01); // cmp byte [ebx+20], 1
		code.Jump(0x74, "hud.owner"); // je .owner
		code.Bytes(0x80, 0x7B, 0x14, 0x03); // cmp byte [ebx+20], 3
		code.Jump(0x75, "hud.next"); // jne .next
		code.Bytes(0x83, 0x7B, 0x0C, 0x34); // cmp dword [ebx+12], 52
		code.Jump(0x72, "hud.next"); // jb .next (only show the first eight updates after release)
		code.Mark("hud.owner");
		code.Bytes(0x8B, 0x03, 0x85, 0xC0); // mov eax, [ebx]; test eax, eax
		code.Jump(0x74, "hud.next"); // je .next
		code.Bytes(0x0F, 0xB6, 0x40, 0x12); // movzx eax, byte [eax+0x12]
		code.Bytes([0x0F, 0xB6, 0x2D], sites.HudSide); // movzx ebp, byte [hudSide]
		code.Bytes(0x33, 0xC5, 0x3B, 0xC2); // xor eax, ebp; cmp eax, edx
		code.Jump(0x74, "hud.show"); // je .show
		code.Mark("hud.next");
		code.Bytes(0x83, 0xC3, 0x20); // add ebx, 32
		code.Bytes(0x49); // dec ecx
		code.Jump(0x75, "hud.scan"); // jne .scan
		code.JumpTo("hud.done");
		code.Mark("hud.show");
		code.Bytes(0xDB, 0x43, 0x08); // fild dword [ebx+8]
		code.AddressOf([0xD8, 0x0D], "hudScale"); // fmul dword [hudScale]
		code.Bytes(0xD9, 0x5C, 0x24, 0x24); // fstp dword [original esp], the fill argument
		code.Mark("hud.done");
		code.Bytes(0x61, 0x9D); // popad; popfd
		code.CallTo(sites.HudDraw);
		code.JumpTo(sites.HudRender + 5);

		code.Mark("kick");
		code.Bytes(0x9C); // pushfd
		code.Bytes(0x60); // pushad
		code.Bytes(0x8B, 0x74, 0x24, 0x28); // mov esi, [esp+40]
		code.Bytes(0x80, 0x7E, 0x16, 0x05); // cmp byte [esi+0x16], 5
		code.Jump(0x75, "kick.done"); // jne .done
		code.Bytes(0x83, 0x7C, 0x24, 0x38, 0x00); // cmp dword [esp+56], 0
		code.Jump(0x75, "kick.done"); // jne .done
		code.Bytes(0x0F, 0xB6, 0x46, 0x47); // movzx eax, byte [esi+0x47]
		code.Bytes(0x83, 0xF8, 0x08); // cmp eax, 8
		code.Jump(0x73, "kick.done"); // jae .done
		code.Bytes(0xC1, 0xE0, 0x05); // shl eax, 5
		code.AddressOf([0x05], "slots"); // add eax, slots
		code.Bytes(0x39, 0x30); // cmp [eax], esi
		code.Jump(0x75, "kick.done"); // jne .done
		code.Bytes(0x80, 0x78, 0x14, 0x03); // cmp byte [eax+20], 3
		code.Jump(0x75, "kick.done"); // jne .done
		code.Bytes(0x8B, 0x48, 0x08); // mov ecx, [eax+8]
		code.Bytes(0xC7, 0x00, 0x00, 0x00, 0x00, 0x00); // mov dword [eax], 0
		code.Bytes(0xC7, 0x40, 0x04, 0x00, 0x00, 0x00, 0x00); // mov dword [eax+4], 0
		code.Bytes(0xC6, 0x40, 0x14, 0x00); // mov byte [eax+20], 0
		code.Bytes(0x83, 0xE9, 0x08); // sub ecx, 8
		code.Jump(0x79, "kick.above_minimum"); // jns .above_minimum
		code.Bytes(0x31, 0xC9); // xor ecx, ecx
		code.Mark("kick.above_minimum");
		code.Bytes(0xC1, 0xE9, 0x02); // shr ecx, 2
		code.Bytes(0x81, 0xC1, 0x00, 0x01, 0x00, 0x00); // add ecx, 256
		code.Bytes(0x8B, 0x54, 0x24, 0x30); // mov edx, [esp+48]
		code.Bytes(0x0F, 0xAF, 0xD1); // imul edx, ecx
		code.Bytes(0xC1, 0xFA, 0x08); // sar edx, 8
		code.Bytes(0x89, 0x54, 0x24, 0x30); // mov [esp+48], edx
		code.Mark("kick.done");
		code.Bytes(0x61); // popad
		code.Bytes(0x9D); // popfd
		if (kickNext != 0) code.JumpTo(kickNext);
		else
		{
			code.Bytes(0x53); // push ebx
			code.Bytes(0x55); // push ebp
			code.Bytes(0x56); // push esi
			code.Bytes(0x8B, 0x74, 0x24, 0x10); // mov esi, [esp+0x10]
			code.JumpTo(sites.Kick + 7); // Resume the kick after its prologue.
		}
		code.Mark("lead_x");
		code.Bytes(0x8B, 0x87, 0xA8, 0x00, 0x00, 0x00); // mov eax, [edi+0xA8]
		code.CallTo("scale_lead"); // call scale_lead
		code.JumpTo(sites.LeadFirst + 6); // Resume after the first lead read.
		code.Mark("lead_z");
		code.Bytes(0x50); // push eax
		code.Bytes(0x8B, 0x87, 0xA8, 0x00, 0x00, 0x00); // mov eax, [edi+0xA8]
		code.CallTo("scale_lead"); // call scale_lead
		code.Bytes(0x89, 0xC1); // mov ecx, eax
		code.Bytes(0x58); // pop eax
		code.JumpTo(sites.LeadSecond + 6); // Resume after the second lead read.
		code.Mark("scale_lead");
		code.Bytes(0x9C); // pushfd
		code.Bytes(0x51); // push ecx
		code.Bytes(0x52); // push edx
		code.AddressOf([0x8B, 0x0D], "leadState"); // mov ecx, [lead_state]
		code.Bytes(0x85, 0xC9); // test ecx, ecx
		code.Jump(0x74, "scale_lead.configured"); // jz .configured
		code.Bytes(0x0F, 0xB7, 0x09); // movzx ecx, word [ecx]
		code.Bytes(0x85, 0xC9); // test ecx, ecx
		code.Jump(0x75, "scale_lead.scale_base"); // jnz .scale_base
		code.Mark("scale_lead.configured");
		code.AddressOf([0x8B, 0x0D], "leadPercent"); // mov ecx, [lead_percent]
		code.Mark("scale_lead.scale_base");
		code.Bytes(0x83, 0xF9, 0x64); // cmp ecx, 100
		code.Jump(0x74, "scale_lead.charge"); // je .charge
		code.Bytes(0x0F, 0xAF, 0xC1); // imul eax, ecx
		code.Bytes(0x99); // cdq
		code.Bytes(0xB9, 0x64, 0x00, 0x00, 0x00); // mov ecx, 100
		code.Bytes(0xF7, 0xF9); // idiv ecx
		code.Mark("scale_lead.charge");
		code.Bytes([0x80, 0x3D], sites.Restart).Bytes(0x00); // cmp byte [restart_type], 0
		code.Jump(0x75, "scale_lead.done"); // jne .done
		code.Bytes(0x0F, 0xB6, 0x4F, 0x47); // movzx ecx, byte [edi+0x47]
		code.Bytes(0x83, 0xF9, 0x08); // cmp ecx, 8
		code.Jump(0x73, "scale_lead.done"); // jae .done
		code.Bytes(0xC1, 0xE1, 0x05); // shl ecx, 5
		code.AddressOf([0x39, 0xB9], "slots"); // cmp [slots+ecx], edi
		code.Jump(0x75, "scale_lead.done"); // jne .done
		code.AddressOf([0x80, 0xB9], "slotPhase").Bytes(0x03); // cmp byte [slots+ecx+20], 3
		code.Jump(0x75, "scale_lead.done"); // jne .done
		code.AddressOf([0xF7, 0x81], "slotButton").Bytes(0x00, 0x10, 0x00, 0x00); // test dword [slots+ecx+4], triangle_button
		code.Jump(0x74, "scale_lead.done"); // jz .done
		code.AddressOf([0x8B, 0x89], "slotCharge"); // mov ecx, [slots+ecx+8]
		code.Bytes(0xC1, 0xE9, 0x02); // shr ecx, 2
		code.Bytes(0x81, 0xC1, 0xE0, 0x00, 0x00, 0x00); // add ecx, 224
		code.Bytes(0x0F, 0xAF, 0xC1); // imul eax, ecx
		code.Bytes(0xC1, 0xF8, 0x08); // sar eax, 8
		code.Mark("scale_lead.done");
		code.Bytes(0x5A); // pop edx
		code.Bytes(0x59); // pop ecx
		code.Bytes(0x9D); // popfd
		code.Bytes(0xC3); // ret

		code.Mark("hudScale").Bytes([], BitConverter.SingleToInt32Bits(1f / 128));
		code.Mark("shortFrames").Bytes([], options.ShortPassFrames);
		code.Mark("throughFrames").Bytes([], options.ThroughPassFrames);
		code.Mark("loftedFrames").Bytes([], options.LoftedThroughFrames);
		code.Mark("leadPercent").Bytes([], options.PassLead);
		code.Mark("leadState").Bytes([], leadState);

		// Eight 32-byte controller records. The labels name fields in the first record.
		code.Mark("slots").Bytes(new byte[4]);
		code.Mark("slotButton").Bytes(new byte[4]);
		code.Mark("slotCharge").Bytes(new byte[12]);
		code.Mark("slotPhase").Bytes(new byte[8 * 32 - 20]);

		byte[] payload = code.Build();
		return new(payload, new[] { "dispatchEntry", "kickEntry", "leadXEntry", "leadZEntry", "hudEntry" }
			.Select(code.AddressOfLabel).ToArray(), code.AddressOfLabel("shortFrames"));
	}

	internal static void Apply(PeImage image, PassGaugeSites sites, PatchOptions options, int leadState, int kickNext)
	{
		var probe = Build(0, sites, options, leadState, kickNext);
		// Keep the mutable controller records in their own section.
		var section = image.AddSection(".passbar", probe.Bytes.Length)
			?? throw new PatchException([Loc.T("Error.PassGaugeUnavailable")]);
		image.MakeWritable(section);
		var payload = Build(section.Va, sites, options, leadState, kickNext);
		image.Write(section.Va, payload.Bytes);

		(int Address, int Length)[] hooks = [(sites.Dispatch, 9), (sites.Kick, 7), (sites.LeadFirst, 6), (sites.LeadSecond, 6), (sites.HudRender, 5)];
		for (int i = 0; i < hooks.Length; i++)
			image.Write(hooks[i].Address, CodeBuilder.JumpPatch(hooks[i].Address, payload.Entries[i], hooks[i].Length));
	}
}
