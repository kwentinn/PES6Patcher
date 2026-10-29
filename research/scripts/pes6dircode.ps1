# Machine code for free (non 8-way) stick direction.
# 0x4B27E0(bits) turns the 8-way direction bits into a heading (its body is virtualised by
# the exe protection). The cave calls it unchanged, then - only when the bits are the
# current human input (0x3BE09FC) and match a pad's analog stick - adds the offset between
# that stick's precise angle (0x1016680 + pad*4) and the centre of its 45-degree sector.
# -Flip subtracts the offset instead, in case the two angle conventions turn opposite ways.
# Returns @{ Cave; CaveBytes; Entry; EntryBytes; Original; Stub; StubBytes }
param([switch]$Flip)

$Entry   = 0x4B27E0
$Cave    = 0x404338          # 0xCC run of 159 bytes starting at 0x404337
$Rotate  = 0x4B2710          # bits -> bits for the attacking side (cdecl, byte arg)
$Original = [byte[]](0x83, 0xEC, 0x10, 0x8B, 0x4C, 0x24, 0x14)   # sub esp,0x10 ; mov ecx,[esp+0x14]

$items = New-Object System.Collections.ArrayList
function B([byte[]]$bytes) { [void]$items.Add(@{ Kind = 'b'; Bytes = $bytes }) }
function L([string]$name)  { [void]$items.Add(@{ Kind = 'l'; Name = $name }) }
function J([byte]$op, [string]$to) { [void]$items.Add(@{ Kind = 'j'; Op = $op; To = $to }) }      # short jcc/jmp
function C($target)        { [void]$items.Add(@{ Kind = 'c'; Target = $target }) }               # call rel32 (address or label)
function D32([int]$v) { [BitConverter]::GetBytes($v) }

B @(0x8B, 0x44, 0x24, 0x04)                   # mov eax,[esp+4]       ; bits argument
B @(0x50)                                     # push eax
C 'stub'                                      # call stub (original body, placed after the code)
B @(0x83, 0xC4, 0x04)                         # add esp,4
B @(0x53, 0x51, 0x52, 0x56, 0x57)             # push ebx/ecx/edx/esi/edi
B @(0x8B, 0xD8)                               # mov ebx,eax           ; original heading
B @(0x0F, 0xB6, 0x4C, 0x24, 0x18)             # movzx ecx,byte [esp+0x18]
B (@(0x81, 0xE1) + (D32 0xF0))                # and ecx,0xF0
J 0x74 'done'                                 # jz done
B (@(0x0F, 0xB6, 0x15) + (D32 0x03BE09FC))    # movzx edx,byte [0x3BE09FC]
B (@(0x81, 0xE2) + (D32 0xF0))                # and edx,0xF0
B @(0x3B, 0xCA)                               # cmp ecx,edx
J 0x75 'done'                                 # jne done
B @(0x33, 0xF6)                               # xor esi,esi           ; pad index
L 'loop'
B (@(0x80, 0x3C, 0xB5) + (D32 0x01016682) + @(0x00))   # cmp byte [esi*4+0x1016682],0
J 0x74 'next'                                 # je next
B (@(0x0F, 0xB6, 0x14, 0xB5) + (D32 0x01016683))       # movzx edx,byte [esi*4+0x1016683]
B @(0x51, 0x52)                               # push ecx ; push edx
C $Rotate                                     # call 0x4B2710
B @(0x83, 0xC4, 0x04, 0x59)                   # add esp,4 ; pop ecx
B @(0x0F, 0xB6, 0xC0)                         # movzx eax,al
B (@(0x25) + (D32 0xF0))                      # and eax,0xF0
B @(0x3B, 0xC1)                               # cmp eax,ecx
J 0x74 'found'                                # je found
L 'next'
B @(0x46, 0x83, 0xFE, 0x08)                   # inc esi ; cmp esi,8
J 0x7C 'loop'                                 # jl loop
J 0xEB 'done'                                 # jmp done
L 'found'
B (@(0x0F, 0xB7, 0x04, 0xB5) + (D32 0x01016680))       # movzx eax,word [esi*4+0x1016680]
B @(0x8B, 0xD0)                               # mov edx,eax
B (@(0x81, 0xC2) + (D32 0x1000))              # add edx,0x1000
B (@(0x81, 0xE2) + (D32 0xE000))              # and edx,0xE000
B @(0x2B, 0xC2)                               # sub eax,edx           ; offset in sector
if ($Flip) { B @(0xF7, 0xD8) } else { B @(0x90, 0x90) }  # neg eax (optional)
B @(0x03, 0xD8)                               # add ebx,eax
L 'done'
B @(0x8B, 0xC3)                               # mov eax,ebx
B @(0x5F, 0x5E, 0x5A, 0x59, 0x5B)             # pop edi/esi/edx/ecx/ebx
B @(0xC3)                                     # ret
# original body of 0x4B27E0: sub esp,0x10 ; mov ecx,[esp+0x14] ; xor eax,eax ; mov [esp],ax ; jmp [0x44658C8]
L 'stub'
B @(0x83, 0xEC, 0x10, 0x8B, 0x4C, 0x24, 0x14, 0x33, 0xC0, 0x66, 0x89, 0x04, 0x24, 0xFF, 0x25, 0xC8, 0x58, 0x46, 0x04)

# two-pass assembly
$labels = @{}
$pos = 0
foreach ($it in $items) {
    switch ($it.Kind) { 'b' { $pos += $it.Bytes.Length } 'l' { $labels[$it.Name] = $pos } 'j' { $pos += 2 } 'c' { $pos += 5 } }
}
$code = New-Object System.Collections.Generic.List[byte]
foreach ($it in $items) {
    switch ($it.Kind) {
        'b' { $code.AddRange([byte[]]$it.Bytes) }
        'j' {
            $rel = $labels[$it.To] - ($code.Count + 2)
            if ($rel -lt -128 -or $rel -gt 127) { throw "saut trop long vers $($it.To)" }
            $code.Add($it.Op); $code.Add([byte]($rel -band 0xFF))
        }
        'c' {
            $t = if ($it.Target -is [string]) { $Cave + $labels[$it.Target] } else { $it.Target }
            $code.Add(0xE8); $code.AddRange([BitConverter]::GetBytes([int]($t - ($Cave + $code.Count + 4))))
        }
    }
}
if ($Cave + $code.Count -gt 0x404337 + 159) { throw "code trop long ($($code.Count) octets)" }

$entryJmp = [byte[]](@(0xE9) + [BitConverter]::GetBytes([int]($Cave - ($Entry + 5))) + @(0x90, 0x90))
@{
    Cave = $Cave; CaveBytes = $code.ToArray()
    Entry = $Entry; EntryBytes = $entryJmp; Original = $Original
}
