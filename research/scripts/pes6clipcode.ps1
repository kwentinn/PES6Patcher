# Machine code for the clip-rectangle fix: 0x40D740(x0, y0, x1, y1) converts 2D coordinates
# to pixels with x * screenWidth / 512. The cave rescales x0 and x1 by 512 / (2 * Half)
# before running the original code, so the 2D clip matches the widened 2D layout.
# Returns @{ Cave = address; CaveBytes = ...; Entry = 0x40D740; EntryBytes = ...; Original = ... }
param([int]$Half = 398)

$Cave  = 0x403500   # inside a 196-byte run of 0xCC padding in .text
$Entry = 0x40D740
$Resume = 0x40D74A  # first instruction after the 10 replaced bytes
$GetWidth = 0x405960

function Rel([int]$from, [int]$to) { [BitConverter]::GetBytes([int]($to - ($from + 5))) }

$code = New-Object System.Collections.Generic.List[byte]
function Emit([byte[]]$bytes) { $code.AddRange($bytes) }

Emit @(0x50, 0x52, 0x51)                        # push eax; push edx; push ecx
Emit @(0x8B, 0x44, 0x24, 0x10)                  # mov eax, [esp+0x10]  ; x0
Emit @(0x69, 0xC0, 0x00, 0x02, 0x00, 0x00)      # imul eax, eax, 512
Emit @(0x99)                                    # cdq
Emit (@(0xB9) + [BitConverter]::GetBytes([int](2 * $Half)))   # mov ecx, 2*Half
Emit @(0xF7, 0xF9)                              # idiv ecx
Emit @(0x89, 0x44, 0x24, 0x10)                  # mov [esp+0x10], eax
Emit @(0x8B, 0x44, 0x24, 0x18)                  # mov eax, [esp+0x18]  ; x1
Emit @(0x69, 0xC0, 0x00, 0x02, 0x00, 0x00)      # imul eax, eax, 512
Emit @(0x99)                                    # cdq
Emit @(0xF7, 0xF9)                              # idiv ecx
Emit @(0x89, 0x44, 0x24, 0x18)                  # mov [esp+0x18], eax
Emit @(0x59, 0x5A, 0x58)                        # pop ecx; pop edx; pop eax
Emit @(0x51, 0x53, 0x55, 0x56, 0x57)            # original: push ecx/ebx/ebp/esi/edi
Emit (@(0xE8) + (Rel ($Cave + $code.Count) $GetWidth))   # original: call 0x405960
Emit (@(0xE9) + (Rel ($Cave + $code.Count) $Resume))     # jmp 0x40D74A

@{
    Cave       = $Cave
    CaveBytes  = $code.ToArray()
    Entry      = $Entry
    EntryBytes = [byte[]](@(0xE9) + (Rel $Entry $Cave) + @(0x90, 0x90, 0x90, 0x90, 0x90))
    Original   = [byte[]](0x51, 0x53, 0x55, 0x56, 0x57, 0xE8, 0x16, 0x82, 0xFF, 0xFF)
}
