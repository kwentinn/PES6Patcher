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
        ShotAccuracyState = 20, SpreadState = 22, SpeedGapState = 24, SprintTurnState = 26, TackleState = 28, PassLeadState = 30, CloseDribbleStrengthState = 44,
        HeadersState = 6;

    /// <summary>Headers percent read at run time (F11), in the test keys state.</summary>
    public static int HeadersStateAddress(FreeKickSites testKeys) => testKeys.State + HeadersState;

    public static TestKey HeadersTestKey(int percent, int texts, int stride) =>
        new(HeadersKey, HeadersState, percent, PatchOptions.MinHeaders, PatchOptions.MaxHeaders, texts, stride);

    /// <summary>Headers of the human teams: <see cref="Percent"/> % of the game's speed and height; <see cref="StateAddress"/> is the F11 word (0 = no test key).</summary>
    public sealed record HeaderFix(int Percent, int StateAddress);

    /// <summary>Player states (+0x16) of the headers (seen in the kick probe: standing, jumping, diving).</summary>
    private const byte HeaderStateA = 30, HeaderStateB = 34, HeaderStateC = 48;

    /// <summary>
    /// Headers of a human team (see <see cref="EmitHumanTeams"/>): at the kick function's entry (player, direction, speed,
    /// height, flag), the speed and the height are scaled to a percent. The AI's headers keep the game's.
    /// </summary>
    private static void EmitHeaders(CodeBuilder code, ShotSites humans, HeaderFix headers)
    {
        code.Bytes(0x60)                                            // pushad                ; the arguments are at esp+0x24...
            .Bytes(0x8B, 0x74, 0x24, 0x24)                          // mov esi,[esp+0x24]    ; player
            .Bytes(0x0F, 0xB6, 0x46, 0x16)                          // movzx eax,byte [esi+0x16] ; state
            .Bytes(0x3C, HeaderStateA).Jump(0x74, "headerState")    // cmp al,30 ; je headerState
            .Bytes(0x3C, HeaderStateB).Jump(0x74, "headerState")    // cmp al,34 ; je headerState
            .Bytes(0x3C, HeaderStateC).Jump(0x75, "headerDone")     // cmp al,48 ; jne headerDone
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
        code.Bytes(0x6A, 0x64, 0x5B)                                // push 100 ; pop ebx
            .Bytes(0x8B, 0x44, 0x24, 0x2C)                          // mov eax,[esp+0x2C]    ; speed
            .Bytes(0x0F, 0xAF, 0xC1, 0x99, 0xF7, 0xFB)              // imul eax,ecx ; cdq ; idiv ebx
            .Bytes(0x89, 0x44, 0x24, 0x2C)                          // mov [esp+0x2C],eax
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
        int testKeysSetUp = 0, ShotSites? humans = null, HeaderFix? headers = null)
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
            .Mark("weightDone")
            .JumpTo(sites.WeightAdd + 6)                            // jmp back
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
    public static Hook KickEntryExtras(int cave, int kickEntry, FreeKickSites? testKeys, int setUp, ShotSites? humans, HeaderFix? headers = null)
    {
        var code = new CodeBuilder(cave);
        if (testKeys != null && setUp != 0) TestKeysSetUpCall(code, testKeys, setUp);
        if (humans != null) EmitHumanTeams(code, humans);
        if (humans != null && headers != null) EmitHeaders(code, humans, headers);
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
    /// Pressing challenge with F8: the reach test and the attempt parameter (cmp edi,0xA0 ; jge fail ; push 0x50) jump here
    /// to scale both by the percent read at run time (<paramref name="percent"/> until F8 is pressed).
    /// </summary>
    public static Hook Tackle(int cave, TackleSites sites, int percent, FreeKickSites testKeys)
    {
        var bytes = new CodeBuilder(cave)
            .Bytes([0x0F, 0xB7, 0x05], testKeys.State + TackleState)  // movzx eax,word [percent]
            .Bytes(0x85, 0xC0)                                      // test eax,eax
            .Jump(0x75, "percentSet")                               // jne percentSet
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
    /// Shot accuracy stat (7 bits, byte at this offset of a player's stats: 98 for a top striker, 46 for a keeper).
    /// The game draws its misses against it, those under pressure included (0x545C20, 0x5B9CB1). Up to 75 the
    /// accuracy share is kept; above, it shrinks by 4 % a point (50 % at 88, 20 % at 95), never below
    /// <see cref="AccuracyFloor"/> %: a top striker under pressure no longer sends the ball into the stands.
    /// </summary>
    public const int ShotAccuracyStat = 0x12;
    public const byte AccuracyFloor = 20;

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
