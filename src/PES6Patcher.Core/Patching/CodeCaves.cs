namespace PES6Patcher.Core.Patching;

/// <summary>A hook (<see cref="NameKey"/> = resource key of its name): original bytes at <see cref="Entry"/> replaced by a jump to code placed in a cave.</summary>
internal sealed record Hook(string NameKey, int Entry, byte[] EntryBytes, int Cave, byte[] CaveBytes);

/// <summary>
/// Code added to PES6.exe. Every address comes from <see cref="ExeLocator"/>, so the same code works
/// in builds where the game code lives elsewhere. See research/NOTES.md for how each fix was found.
/// </summary>
internal static class CodeCaves
{
    public const int ClipSize = 60, CursorSize = 19, FreeDirectionSize = 156;

    /// <summary>Kick relay cave: dribble direction part, close dribbling part, replayed bytes and jump back.</summary>
    public const int RelayDirectionSize = 79, RelayCloseDribbleSize = 180, RelayTailSize = 11;

    /// <summary>Key that switches close dribbling on and off in game.</summary>
    public const byte CloseDribbleSwitchKey = 0x70;   // VK_F1

    public static int KickRelaySize(bool direction, bool closeDribble) =>
        (direction ? RelayDirectionSize : 0) + (closeDribble ? RelayCloseDribbleSize : 0) + RelayTailSize;

    /// <summary>Close dribbling: touch speed x 14/16 while running, x 13/16 while sprinting (tuned in game).</summary>
    public const byte RunTouchSixteenths = 14, SprintTouchSixteenths = 13;

    /// <summary>
    /// The close dribbling reduction follows the player's dribble accuracy: none up to <see cref="MinAccuracy"/>
    /// (the game's own touches), full from <see cref="MaxAccuracy"/>, in proportion between.
    /// </summary>
    public const byte MinAccuracy = 65, MaxAccuracy = 95;

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
    /// <item>close dribbling: for the dribble touches, scales the speed argument down (shorter touches).</item>
    /// </list>
    /// Then it replays the 6 replaced bytes (mov ecx,[esp+0xC] ; mov eax,ecx). Passes and shots are left alone.
    /// </summary>
    public static Hook KickRelay(int cave, DribbleSites sites, DirectionSites? direction, bool closeDribble, int switchSetUp = 0)
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
                // speed -= speed * (16 - sixteenths) / 16 * (accuracy - min) / (max - min), accuracy capped at max
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
                .Bytes(0x0F, 0xAF, 0xC1)                            // imul eax,ecx
                .Bytes(0x0F, 0xAF, 0x44, 0x24, 0x0C)                // imul eax,[esp+0xC]   ; speed
                .Bytes(0x99)                                        // cdq
                .Bytes([0xB9], 16 * (MaxAccuracy - MinAccuracy))    // mov ecx,16*(max-min)
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
