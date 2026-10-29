# Build a copy of PES6.exe whose 2D menus/HUD are drawn without horizontal stretching.
# Half = virtual half-width: 398 for 16:9, 796 for 32:9 (original game: 256).
# Every patched location is checked against the original bytes first; nothing is written
# if any check fails.
# -WidthFn makes the 2D width getter return the new width (2D layout and clipping), while
# the two calls that feed it to the 3D projection (0xF84064) keep 512.
# -Templates and -Suspects add patches that made players slide; they are off by default.
# -FreeDir lets the human player run along the analog stick's precise angle instead of the
# 8 directions, and lowers the 45-degree turn threshold of the running state to ~17 degrees.
# Usage: pes6patch.ps1 -In PES6.exe -Out PES6_ws.exe -Half 398 -WidthFn [-FreeDir]
param([string]$In, [string]$Out, [int]$Half = 398, [switch]$WidthFn, [switch]$Templates, [switch]$Suspects, [switch]$FreeDir)

$ErrorActionPreference = 'Stop'
$b = [IO.File]::ReadAllBytes($In)

# VA -> file offset: .text is raw 0x400 for RVA 0x1000; .rdata/.data are raw == RVA.
function Off([int]$va) { if ($va -lt 0xB7D000) { $va - 0x400C00 } else { $va - 0x400000 } }

$errors = @()
function Expect([int]$va, [byte[]]$bytes, [string]$what) {
    $o = Off $va
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($b[$o + $i] -ne $bytes[$i]) { $script:errors += ("{0} @0x{1:X}: octets inattendus" -f $what, $va); return $false }
    }
    $true
}
function Put([int]$va, [byte[]]$bytes) { [Array]::Copy($bytes, 0, $b, (Off $va), $bytes.Length) }
function F([single]$v) { [BitConverter]::GetBytes($v) }
function I32([int]$v) { [BitConverter]::GetBytes($v) }
function I16([int]$v) { [BitConverter]::GetBytes([int16]$v) }

$HalfConst = 0x00B8AD44; $InvConst = 0x00B84978
$NewHalf   = 0x00BC3F00; $NewInv   = 0x00BC3F04   # zero padding at the end of .rdata

$HalfReaders = 0x78ED90, 0x78F038, 0x78F0D7,
               0x867BE2, 0x867C0B, 0x867C40, 0x867C69,
               0x87AEDC, 0x8B6E9D, 0x8B6EC9,
               0x922EA5, 0x941B98
$InvReaders  = 0x87AEE4
# Readers that write match state (0x3BCDCBC, 0x3BE12AC) or object fields +0xE0/+0xE8.
if ($Suspects) {
    $HalfReaders += 0x656AF7, 0x656BD9, 0x656F0D, 0x657A2E, 0x6D1AE9, 0x8DBAA0, 0x8DC270, 0x8DC319, 0x8DC3F8
    $InvReaders  += 0x656821
}
$TemplateFields = 0xD4FB98, 0xD4FBD0, 0xD4FC08, 0xD4FC40, 0xD4FC78, 0xD4FCB0, 0xD4FCE8, 0xD4FD20

$plan = @()

# 1. private constants in .rdata padding
if (Expect $NewHalf (New-Object byte[] 8) 'emplacement libre') {
    $plan += ,@($NewHalf, (F $Half)); $plan += ,@($NewInv, (F (1.0 / (2 * $Half))))
}

# 2. 2D width getter: mov eax, 0x200 ; ret
if ($WidthFn -and (Expect 0x8744F0 ([byte[]](0xB8, 0x00, 0x02, 0x00, 0x00, 0xC3)) 'largeur 2D')) {
    $plan += ,@(0x8744F1, (I32 (2 * $Half)))
    # call 0x8744F0 right before "push eax; call 0x409A50" -> mov eax, 0x200 (same 5 bytes)
    if (Expect 0x661123 ([byte[]](0xE8, 0xC8, 0x33, 0x21, 0x00)) 'appel largeur 3D (0x6610B0)') { $plan += ,@(0x661123, [byte[]](0xB8, 0x00, 0x02, 0x00, 0x00)) }
    if (Expect 0xB3F939 ([byte[]](0xE8, 0xB2, 0x4B, 0xD3, 0xFF)) 'appel largeur 3D (0xB3F910)') { $plan += ,@(0xB3F939, [byte[]](0xB8, 0x00, 0x02, 0x00, 0x00)) }
    # clip rectangle -> pixels (0x40D740) still divides x by 512: rescale x in a code cave
    $clip = & (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'pes6clipcode.ps1') -Half $Half
    $pad = [byte[]](@(0xCC) * $clip.CaveBytes.Length)
    if ((Expect $clip.Cave $pad 'zone libre du correctif de decoupage') -and (Expect $clip.Entry $clip.Original 'entree du decoupage (0x40D740)')) {
        $plan += ,@($clip.Cave, $clip.CaveBytes); $plan += ,@($clip.Entry, $clip.EntryBytes)
    }
    # player cursor: 3D -> 2D layer projection (0xF840A0) x scale still assumes 512 units
    $cur = & (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'pes6cursorcode.ps1') -Half $Half
    $curPad = [byte[]](@(0xCC) * $cur.CaveBytes.Length)
    if ((Expect $cur.Const (New-Object byte[] 4) 'emplacement libre (curseur)') -and
        (Expect $cur.Cave $curPad 'zone libre du correctif du curseur') -and
        (Expect $cur.Entry $cur.Original 'projection du curseur (0x40CE95)')) {
        $plan += ,@($cur.Const, $cur.ConstBytes); $plan += ,@($cur.Cave, $cur.CaveBytes); $plan += ,@($cur.Entry, $cur.EntryBytes)
    }
}

# 3. instruction operands -> private constants
foreach ($pair in @(@($HalfReaders, $HalfConst, $NewHalf), @($InvReaders, $InvConst, $NewInv))) {
    foreach ($ins in $pair[0]) {
        $o = Off $ins; $at = -1
        for ($i = 1; $i -le 4; $i++) { if ([BitConverter]::ToInt32($b, $o + $i) -eq $pair[1]) { $at = $ins + $i; break } }
        if ($at -lt 0) { $errors += ("operande introuvable @0x{0:X}" -f $ins) } else { $plan += ,@($at, (I32 $pair[2])) }
    }
}

# 3b. free stick direction
if ($FreeDir) {
    $dir = & (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'pes6dircode.ps1')
    $dirPad = [byte[]](@(0xCC) * $dir.CaveBytes.Length)
    if ((Expect $dir.Cave $dirPad 'zone libre des directions') -and (Expect $dir.Entry $dir.Original 'entree direction (0x4B27E0)')) {
        $plan += ,@($dir.Cave, $dir.CaveBytes); $plan += ,@($dir.Entry, $dir.EntryBytes)
    }
    # running state: "cmp si,0x20" (45 degrees) before starting a turn -> 0x0C (~17 degrees)
    if (Expect 0x5AF3CC ([byte[]](0x66, 0x83, 0xFE, 0x20)) 'seuil de virage (0x5AF3CC)') { $plan += ,@(0x5AF3CF, [byte[]](0x0C)) }
}

# 4. 2D view rectangles (x, y, w, h) initial values
foreach ($r in 0xBEE2D4, 0xBEE2E4) {
    if (Expect $r ((F -256) + (F -224) + (F 512) + (F 448)) 'rectangle 2D') {
        $plan += ,@($r, (F (-$Half))); $plan += ,@(($r + 8), (F (2 * $Half)))
    }
}

# 5. projection templates: int16 width 512 followed by int16 height 448
if ($Templates) {
    foreach ($t in $TemplateFields) {
        if (Expect $t ((I16 512) + (I16 448)) 'table de projection') { $plan += ,@($t, (I16 (2 * $Half))) }
    }
}

if ($errors) { $errors; Write-Error "Aucun fichier ecrit : l'exe ne correspond pas a la version analysee."; exit 1 }
foreach ($p in $plan) { Put $p[0] $p[1] }
[IO.File]::WriteAllBytes($Out, $b)
"{0} modifications appliquees -> {1}" -f $plan.Count, $Out
