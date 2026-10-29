namespace PES6Patcher.Core.Patching;

/// <summary>A hook: original bytes at <see cref="Entry"/> replaced by a jump to code placed in a cave.</summary>
internal sealed record Hook(string Name, int Entry, byte[] Original, byte[] EntryBytes, int Cave, byte[] CaveBytes);

/// <summary>
/// Code added to PES6.exe. Caves live in runs of 0xCC padding inside .text.
/// See research/NOTES.md for how each location was found.
/// </summary>
internal static class CodeCaves
{
    /// <summary>
    /// 0x40D740(x0, y0, x1, y1) converts the 2D clip rectangle to pixels with x * screenWidth / 512.
    /// The cave rescales x0 and x1 by 512 / (2 * half) before running the original code.
    /// </summary>
    public static Hook Clip(int half)
    {
        const int cave = 0x403500, entry = 0x40D740, resume = 0x40D74A, getScreenWidth = 0x405960;
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
            .CallTo(getScreenWidth)                             // original: call 0x405960
            .Build();
        code = [.. code, .. CodeBuilder.JumpPatch(cave + code.Length, resume, 5)];
        return new Hook("découpage des listes", entry,
            [0x51, 0x53, 0x55, 0x56, 0x57, 0xE8, 0x16, 0x82, 0xFF, 0xFF],
            CodeBuilder.JumpPatch(entry, cave, 10), cave, code);
    }

    /// <summary>Float multiplier used by <see cref="Cursor"/>: (2 * half) / 512.</summary>
    public const int CursorScaleAddress = 0x00BC3F08;

    /// <summary>
    /// 0x40CD40 builds the 3D -> 2D layer projection (0xF840A0) with an x scale of
    /// viewportWidth (512) / aspect. The cave multiplies that scale by (2 * half) / 512.
    /// </summary>
    public static Hook Cursor()
    {
        const int cave = 0x403540, entry = 0x40CE95, resume = 0x40CE9D;
        byte[] original = [0xDB, 0x44, 0x24, 0x4C, 0xD8, 0x4C, 0x24, 0x40];   // fild [esp+0x4C] ; fmul [esp+0x40]
        var code = new CodeBuilder(cave)
            .Bytes(original)
            .Bytes([0xD8, 0x0D], CursorScaleAddress)            // fmul dword [CursorScaleAddress]
            .Build();
        code = [.. code, .. CodeBuilder.JumpPatch(cave + code.Length, resume, 5)];
        return new Hook("curseur du joueur", entry, original, CodeBuilder.JumpPatch(entry, cave, 8), cave, code);
    }

    /// <summary>
    /// 0x4B27E0(bits) turns the 8-way direction bits into a heading (its body is virtualised by
    /// the exe protection). The cave calls it unchanged, then - only when the bits are the current
    /// human input (0x3BE09FC) and match a pad's analog stick - adds the offset between that
    /// stick's precise angle (0x1016680 + pad * 4) and the centre of its 45-degree sector.
    /// </summary>
    public static Hook FreeDirection()
    {
        const int cave = 0x404338, entry = 0x4B27E0, rotateBits = 0x4B2710;
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
            .Bytes([0x0F, 0xB6, 0x15], 0x03BE09FC)              // movzx edx,byte [0x3BE09FC]
            .Bytes([0x81, 0xE2], 0xF0)                          // and edx,0xF0
            .Bytes(0x3B, 0xCA)                                  // cmp ecx,edx
            .Jump(0x75, "done")                                 // jne done
            .Bytes(0x33, 0xF6)                                  // xor esi,esi           ; pad index
            .Mark("loop")
            .Bytes([.. new byte[] { 0x80, 0x3C, 0xB5 }, .. BitConverter.GetBytes(0x01016682), 0x00])   // cmp byte [esi*4+0x1016682],0
            .Jump(0x74, "next")                                 // je next
            .Bytes([0x0F, 0xB6, 0x14, 0xB5], 0x01016683)        // movzx edx,byte [esi*4+0x1016683]
            .Bytes(0x51, 0x52)                                  // push ecx ; push edx
            .CallTo(rotateBits)                                 // call 0x4B2710
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
            .Bytes([0x0F, 0xB7, 0x04, 0xB5], 0x01016680)        // movzx eax,word [esi*4+0x1016680]
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
            .Mark("stub")                                       // original body: sub esp,0x10 ; mov ecx,[esp+0x14] ;
            .Bytes(0x83, 0xEC, 0x10, 0x8B, 0x4C, 0x24, 0x14,    //   xor eax,eax ; mov [esp],ax ; jmp [0x44658C8]
                   0x33, 0xC0, 0x66, 0x89, 0x04, 0x24, 0xFF, 0x25, 0xC8, 0x58, 0x46, 0x04)
            .Build();
        return new Hook("directions libres", entry,
            [0x83, 0xEC, 0x10, 0x8B, 0x4C, 0x24, 0x14],
            CodeBuilder.JumpPatch(entry, cave, 7), cave, code);
    }
}
