# Machine code for the player-cursor fix. 0x40CD40 builds the matrix that projects 3D points
# into the 2D layer (0xF840A0) with an x scale of viewportWidth (512) / aspect, centred on 2048.
# The 2D layer now spans 2 * Half units, so that x scale must grow by (2 * Half) / 512.
# Returns @{ Const; ConstBytes; Cave; CaveBytes; Entry; EntryBytes; Original }
param([int]$Half = 398)

$Const  = 0x00BC3F08   # .rdata padding, next to the 2D constants
$Cave   = 0x403540     # same 0xCC run as the clip fix (which ends at 0x40353C)
$Entry  = 0x40CE95
$Resume = 0x40CE9D

function Rel([int]$from, [int]$to) { [BitConverter]::GetBytes([int]($to - ($from + 5))) }

$code = [byte[]](0xDB, 0x44, 0x24, 0x4C,               # fild dword [esp+0x4C]  (original)
                 0xD8, 0x4C, 0x24, 0x40) +             # fmul dword [esp+0x40]  (original)
        [byte[]](0xD8, 0x0D) + [BitConverter]::GetBytes([int]$Const)   # fmul dword [Const]
$code = [byte[]]($code + [byte[]](0xE9) + (Rel ($Cave + $code.Length) $Resume))

@{
    Const      = $Const
    ConstBytes = [BitConverter]::GetBytes([single]((2 * $Half) / 512.0))
    Cave       = $Cave
    CaveBytes  = $code
    Entry      = $Entry
    EntryBytes = [byte[]](@(0xE9) + (Rel $Entry $Cave) + @(0x90, 0x90, 0x90))
    Original   = [byte[]](0xDB, 0x44, 0x24, 0x4C, 0xD8, 0x4C, 0x24, 0x40)
}
