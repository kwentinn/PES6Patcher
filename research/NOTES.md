# PES6 — research notes

*English | [Français](NOTES.fr.md)*

How the PES6 Patcher fixes were found and what they change. "v6" is the first `PES6.exe` build validated in game (widescreen and free direction), in 16:9 and in 32:9, which the app still reproduces byte for byte. The gameplay options added since are described in section 3.

Addresses are those of the reference build (no-DVD `PES6.exe`, SHA-256 `3FE13850…`). The app finds every location by signature, so other builds with the same code are patched too (section 5).

---

## 1. Settings changed (outside the exe), during the 16:9 research

| File | Setting | Value |
|---|---|---|
| `kitserver\kload.cfg` | `dx.fullscreen.width/height`, `internal.resolution.width/height` | 2560 × 1440 |
| `kitserver\speeder.cfg` | `count.factor` | 1.0 (original speed) |
| `kitserver\lodmixer.dat` | manual aspect ratio (bytes 41–44) | 1.77778 (16:9) |
| `dgVoodoo.conf` | `ScalingMode` | `stretched_ar` (centred picture, black bars) |

---

## 2. Changes to `PES6.exe` (v6)

The original is kept as **`PES6.exe.orig`**, next to the game.

PES6's 2D menu layer comes from the PS2: 512 × 448, centred. The game stretched it across the whole screen width. The patch widens that layer to **796 × 448** (16:9). 3D and gameplay keep using the original values.

| Address | Change | Purpose |
|---|---|---|
| `0xBC3F00` / `0xBC3F04` | new constants 398 and 1/796 (`.rdata` padding) | half-width and scale of the new 2D layer |
| 12 instructions (`0x78ED90` … `0x941B98`) and `0x87AEE4` | read 398 and 1/796 instead of 256 and 1/512 | 2D layer → pixels conversion |
| `0x8744F0` | `return 796` instead of `return 512` | 2D width (layout, clipping, cursor) |
| `0x661123`, `0xB3F939` | `call 0x8744F0` → `mov eax, 0x200` | the 3D projection keeps 512 (otherwise the players "slide") |
| `0xBEE2D4`, `0xBEE2E4` | initial 2D view rectangles widened | consistent 2D layer |
| `0x40D740` → added code at `0x403500` (60 bytes) | rescales x by 512/796 | list clipping (`SetViewport`) |
| `0x40CE95` → added code at `0x403540` (19 bytes), constant `0xBC3F08` | x scale × 1.5547 | position of the blue cursor above the player |
| `0x4B27E0` → added code at `0x404338` (156 bytes) | adds the stick's real offset within its 45° sector | free analog stick direction |
| `0x5AF3CF` | turn threshold `0x20` → `0x0C` (45° → about 17°) | small corrections trigger a turn |

Key points:

- The instructions that write into the match data (`0x656xxx`, `0x657A2E`, `0x6D1AE9`, `0x8DBA70`, `0x8DD430`) are **not** redirected: they made the players slide.
- The "projection tables" `0xD4FB98…` are **not** changed, for the same reason.
- The free direction only concerns the human player with an analog stick. The D-pad, the keyboard and the AI are unchanged.

---

## 3. Gameplay options (after v6)

Each option is a hook: a few bytes of the game replaced by a jump to added code in the `CC` padding, which replays the replaced instructions. They were found with hardware breakpoints and memory captures in a running game (section 5), then checked in game.

### Ball kick and dribble touches

- Every touch, pass and shot goes through the **kick function** `0x478020` (player, direction, speed, height, flag). Dribble touches go through a **relay** `0x4781F0` (player, direction, speed, height).
- The dribble touches are recognised by their return address: running `0x5AF6D3`, `0x5AF0CF`, `0x5ADEC6`, `0x5B0D65`, `0x5B07A5`; sprinting `0x5AFB40`, `0x5AFE6D`. The signatures were lengthened because the same code exists in other states.
- **360° dribbling** (free direction): the touch at `0x5ADEC6` gets the stick angle rounded to 22.5° by virtualised code. At the relay's entry, for the pad whose stick gives that rounded direction (or its opposite: attacking side), the rounding is added back. The AI matches no pad and is left alone.
- **Close dribbling**: at the relay's entry, the speed of the 7 dribble touches is scaled down (× 14/16 running, × 13/16 sprinting), in proportion to the player's dribble accuracy (stat `0x3BCF5B2`, 14 bytes before the technique `0x3BCF5C0` that the game reads for the touch direction noise; until 1.26 the patcher read that technique by mistake, as kanzi123 noticed in #32): nothing up to 65, the full reduction from 95.
- **F1** switches close dribbling on and off. The message goes through Kitserver: `kload.dll` exports `HookFunction` (3 = Direct3D `Present`, a stdcall callback with 5 arguments) and `KDrawText` (x, y, colour, size, text, absolute; 640 × 480 base). The state lives in the last 16 bytes of `.rdata` (`0xBC3FF0`), made writable.

### Intro

- Opening movie: the movie function `0x88E350` already returns early when skipping is asked; its `je` at `0x88E361` is made inactive.
- Logos: a logo flagged as skippable ends at once when a button is pressed; both tests (`0x9AFAD5` flag, `0x9AFAE4` button) are made inactive. The loading done during the logos (options file, etc.) still happens.
- Dead ends: skipping start-up state 1 removed the language screen, skipping loading step 1 skipped the options file (crash), and forcing the logo timer froze the game.

### Free kicks

- A direct free kick shot is the set piece's call to the **shot function** at `0x58CC11` (→ `0x5B9130`). Open-play shots call the same function from 18 other places.
- The ball's flight `0x475810` (also used by the trajectory predictions of the keeper and the AI) adds the ball weight with `fadd [0xB8AE70]` at `0x475EEF` (188.0). PityCharly's physics mod rewrites that constant while the game runs, so the extra weight is added on top instead.
- The set piece's call reads the left sticks (table `0x1016680`: angle, magnitude, direction bits, 4 bytes per pad): stick up → the ball of that shot gets the extra weight until the next kick (the kick function's entry clears it). The game reads the stick at the same moment for the shot's spin, and no longer during the flight (ball fields `+0x6A`, `+0x6C`, `+0x74`).

### Shots

- **Gentler shots**: at the shot function's entry, the power argument above 0xC0 (the lowest base of the shot kinds) is scaled. Set pieces keep the game's power.
- **More accurate shots**: the shot's call to the kick function (`0x5BBC9A`) brings the final direction and height back towards the direction asked, saved at the entry. A weak-foot shot keeps the square of the share: the game compares the animation's foot (`[player+4]+0x2D`) with the favoured foot (stat `0x3BCF5A8`, bit 0) at `0x544270`, and gives that shot less power and a larger gap, always to the same side.
- **Under pressure**: `0x545C20` (called at `0x5B9907` and `0x5B9C90`) tells a shot under pressure: an opponent within about 1 m, or within 2 to 4 m and in the shot's line, or about 3 m in line unless a draw against the shooter's shot technique (stat `+0x12`; taken for the shot accuracy until 1.26) passes; a draw between a stat of the defender and the shooter can lift it. Under pressure the direction error is divided by 7 to 9 instead of 9 to 11 (`0x5B9A51`), and a second draw on the shot accuracy changes the power or height (`0x5B9CB1`). Measured: a top striker's shot under pressure from a strong gauge left at a height of 63 (about 30 usually). **More accurate shots** now scales its share by the shot accuracy: kept up to 75, 4 % less a point above, at least 20 %.
- **Weak foot**: the weak foot accuracy (stat `+0x1C`, bits 11 to 13, 0 to 7) gives a factor of 130, 120, 110, 108, 106, 104, 102 or 101 % (`0x4C7EA0`): a human's weak-foot shot loses that excess of power (`0x5B9777`); the AI's shot gauge (`0x544EB0`) grows by it when the weak foot is rated 3 or less (displayed 1 to 4) and a draw on the shot accuracy fails.
- **Human teams**: at every kick, for each pad whose stick is pushed, each team's active player gets +3 when its input direction is the stick's (or the opposite one), −2 otherwise (+1 until 1.22: about 40 kicks were needed, and in a measured match the human's first shot, under pressure in the first minute, left uncorrected at twice the usual height). A team at 32 or more (out of 64) is human. The AI's shots are left alone. The score is kept by the active player's team (`+0x12`): the active player slots follow the ends, and at half-time the teams swap ids (1–11 and 12–22). Until 1.22 the score was kept by slot, so in the second half the human's shots were no longer corrected and the AI's were (measured with breakpoints on the shot's kick call, the height before and after the correction). Uncorrected, a shot from a nearly full gauge can leave with a height of 57 to 65 instead of about 30.
- **Shot kinds**: a shot is player state 4. Its handler `0x5BBF90` switches on `+0x18` (`0x00`…`0x27`, table at `0x5BD858`, cases at `0x5BD800`); 17 values are shot kinds (`0x10`–`0x14`, `0x16`, `0x18`, `0x1B`–`0x1E`, `0x21`–`0x25`, `0x27`), each calling the shot function `0x5B9130` from its own site (`0x5BC321` … `0x5BD7BA`) with its own base power (`0x300` to `0xA00`) and parameters. The kind is chosen by the shot decision code, `0x544954` to `0x54980A` (19 places that set state 4 and `+0x18`), plus `0x579AEB` (kind `0x27`) and the set pieces (`0x58C026`, `0x58C323`). Which kind is which (running shot, volley, overhead kick…) was not mapped: read when a shot starts, `+0x18` held a phase (0 or 1), not the kind.
- The shot function draws many random numbers (`0x483EF0`, `0x483F40`, about 40 calls between `0x5B93CB` and `0x5B9E59`) before the kick call `0x5BBC9A`; that is why the accuracy acts on the kick call's final direction and height rather than on each draw.
- **Lobs**: a lob is a shot whose shooter holds button `0x400` (L1) in `+0xB0` when the ball leaves (all the lobs logged; none of the other shots). Kick probe (`pes6probe.ps1`, branch `kick-probe`): classic lobs use animations `0xC9`, `0xCA`, `0x244`, `0x245` and leave at a height of 45 to 68, against 13 to 35 for shots; badly placed lobs use other animations (`0xF`, `0xEE`) and leave low. The accuracy used to scale every height above 20 as an error, which flattened lobs (half of their height at 50 %, nearly all at 1 %). A lob now keeps 95 % of its height and gets the lob power (70 % by default) on its speed. Tried and dropped: telling lobs by their height (misses the low ones); scaling the power argument at the shot function's entry for lobs (irregular: the lob's strength is decided further on).
- Measured in game with the share at 1 %: shots go "much more on target"; what still misses is the aim itself (the direction asked), not the random gap.
- Dead end: the shot gauge. The power is decided by the shot kind and formulas (for example `0x108 + [player+0xA4] × 24 / (8142 − 16 × [player+0x5C])`), not by a visible counter. The human input is copied into the player (`+0xB0`; bit `0x800` was taken for the shoot press, but measured at the kick it comes with the right shoulders instead — see the super shot below) by protected code (around `0x448A73D`); the AI presses the same bit. `[player+4]+0x34` is the animation frame, not a press counter.
- **Super shot** (L1 + R1, branch `feat/super-shot`): buttons measured at the kick by added code writing a ring of the last shots (`+0xB0` of the shooter, player state 4): nothing held gives `0x20` (the direction alone), L1 `0x400`, R1 `0x01488800`, R2 `0x01000800`, L2 `0x02000200`, and L1 + R1 `0x01000C00` — holding L1 drops R1's own bits (`0x488000`), so only the shared `0x01000800` is left (R1 + R2 cancels the shot). The game's own speed argument measured 18463 to 23011 on strong shots; 36300 was first validated in a match, then found short of bite and raised to 38000; 40000 felt too fast then, but once a star's ball dipped it became the Pro default (2026-11-15: Normal 42000, Hardcore 38000). Setting the speed and the height alone still gave a chip, so the lob had to go: clearing L1 at the protected input copy (`0x448A73D`) does nothing (8 passes in 10 seconds: not the per-frame store). Found with a hardware read breakpoint on the shooter's `+0xB0`: the buttons are rebuilt every frame by `mov ecx,[esi+0xB0] ; mov eax,[esi+0xB4] ; and ecx,400h ; or ecx,eax ; mov [esi+0xB0],ecx`, where only L1 survives — that `and` is the last place the super shot can be told apart — and eight `mov eax,[esi+0xB0] ; test ah,4` decide the lob (seven contiguous, one with `push edi ; xor edi,edi` in between; the set piece one, at the corner wait, is left alone). Hiding L1 from those eight while the flag is up gives the plain shot's animation and contact. Height 34 validated (the game's shots: 13 to 35): lower, the shot is so flat it goes in every time.
- Super shot, what the kick gets (recorded in game for the last kicks taken with L1 held): the player state is **5** when the shot is taken running, 4 only when standing — and since R1 is the sprint button, nearly every super shot is a 5 (a state-4-only test fired on none of them). The kick's **5th argument** is 0 for a shot and 1 for a pass (a pass from the same state measured speed 7424 and height 0, against 26432 / 21 and 20870 / 8 for two shots), which is what tells them apart: a pass played while holding L1 and R1 used to be sent at the shot's speed.
- Super shot, telling a shot from a pass: neither the player state nor the kick's 5th argument is enough (ground passes came as state 27 with a negative height, through passes as state 4 or 5 with the argument at 0). The **return address** at the kick entry is: a shot always returns to the shot function's own call (`0x5BBC9F` here, `ShotSites.KickCall` + 5), while the passes measured returned to `0x71222D7E` — PityCharly's physics mod, which calls the same function for its passes — or to `0x478230`.
- Super shot of a star: the ball keeps extra weight while it flies, so it rises over the defenders and dips under the bar. Tuned in a match on the gravity added every frame (`0xB8AE70`): 140 floated and flew over, 198 (the physics mod's value; the game's own is 188) was the start, 250 then 270 then **300** gave the dip wanted, with the height ending at **32** once the dip was in. 300 from 188 is +60 %, hence the +55 % default on the free kick fix's own unit (1 % of the game's weight). The shooter is a star by his shot accuracy stat (`+0x12`, 7 bits), 90 and above by default.

### Players spread wider

- Where a player places himself goes through the **target setter** `0x4C9DC0` (player, x, z), called by 45 pieces of the positioning AI; its body continues in protected code. Only the players placing themselves (state `+0x16` = 7) use it.
- The added code moves the target away from the centre of the team's targets (`+0x6C` / `+0x70` of its outfield players), across the pitch only (z). Spreading the length (x) too made the defenders drop close to their keeper.

### Speed gap

- The running speed is `[player+0x1C]`. Every frame, `0x48FEF0` moves it towards a target speed given by the caller (sprinting with the ball: `0x4C20F9`), with an acceleration time from stat `0x3BCF5AF`.
- The **maximum speed** `[player+0x58]` is computed, and recomputed during the match, at `0x447BF52` (`.rld` section of the protection, plain code): 7542 + 12 × (top speed − 40) × 100 / 60 − 16 × `[player+0x5C]`. The top speed stat is the byte `0x3BCF5AE` (correlation 1.00 with `+0x58` over 22 players). One stat point is only worth 20 units: 68 → 8094, 97 → 8682.
- The added code scales the stat's part around a player at 75. As the value is recomputed during the match, F6 applies at once.

### Acceleration gap

- `0x48FEF0` is a jump into the protection (`0x44B6A20`); its 70-odd callers pass (player, target speed, acceleration time). Every one computes that time the same way: a base (`0x1C`, `0x1E`, `0x28` or `0x30`) + a player field (`+0x5C` or `+0x5E`) − `0x483E10`(acceleration stat `0x3BCF5AF`, 50) / 10. `0x483E10`(a, b) = (a − b) × 100 / (100 − b) above b, else 0, so the stat only takes 0 to 9 off a time of about 28 to 48.
- The /10 is `mov eax,66666667h ; imul ecx ; sar edx,2`. In memory, `sar edx,1` at the 55 places that follow a read of the stat (x 2) felt right; `sar edx,0` (x 4) made the times of the best players negative: a player crossed the pitch at once.
- **Bigger acceleration gap** writes `sar edx,1` there. The anchor is the sprint with the ball (`0x4C20C4`), which also gives the stat's address.

### Sharp turns when sprinting

Measured with `pes6r turnlog` (one line per change of the ball carrier's fields) and execute breakpoints:

- Running with the ball, a stride lasts about 26 frames (0.46 s, animations 65/66) with one touch near frame 7; the stick's new direction is only taken at the next stride. Sprinting, a stride lasts about 0.67 s (animations 29/30 = `0x1D`/`0x1E`). A turn of more than 112° goes through a turning animation (552, about 0.65 s).
- The sprint stride starts in `0x4E2830` (called from `0x5B7954` when the sprint button is held, `0x4DCBB0` otherwise). It compares the stick direction (`0x4B3010`) with the facing `+0x26`, in 1/256 of a turn: below 8 nothing happens, from `0x50` (112°) the turning animation is used. Otherwise:
  - the **player's direction** turns by a fixed step: animation `0x1E` with 15° (`push 0Fh` at `0x4E299D`) on the favoured foot's side, `0x1D` with 25° (`push 19h` at `0x4E2A51`) on the other, through `0x4F3820`, which goes into the protected code; both branches meet at `0x4E2A7E` with the new direction in `di`, written to `+0x2C` at `0x4E2FA7`;
  - the **ball's direction** is computed at `0x4E2AFF`: `0x4970F0` (turn a direction towards another by at most a given angle) from the facing rounded to 22.5° (`(+0x26 + 0x800) & 0xF000`) towards the stick, by at most `0x1000` (`push 1000h` at `0x4E2AE6`). Logged at the touch (relay call at `0x5AFB3B`, direction from `[player+0x30]`): `0xA000`, `0xB000`, `0xC000`… exactly one 22.5° step per touch, whatever the stick asked. **That step is what makes the turns go "by stages".**
- The player then runs after the ball: `0x5AFD45` points `+0x2C` every frame at the ball's predicted position.
- **Sharper turns when sprinting** replaces the `push 1000h` by the maximum turn of the carrier: 22.5° up to 65 in dribble accuracy (the game's), the chosen maximum (90° by default) from 95, in proportion between. A touch that turns the ball more than 22.5° from its current direction (`[ball+0x58]`) also turns the player's run (`+0x28`) and facing (`+0x26`, `+0x42`) to the touch direction, at both sprint touches: `0x5AFB32` (animation 29) and `0x5AFE5C` (animation 30, the other foot: `xor ecx,ecx ; mov cx,[edi+0x30] ; add esp,0Ch` before the relay call). Until 1.18 only the first was hooked, with a speed loss of up to 40 % instead: on the other foot's touch the player finished his stride straight and only turned at the next one, about 300 ms late. A slowdown of the ball and the player from 45° was tried (30 %, then 15 % at 90°) and dropped with the next point. Snapping the facing at the touch makes the animation jump, so the sprint stride now hands turns from 45° to the normal dribble stride (`cmp ax,50h` at `0x4E287C` → `20h`), which plays the turning animation: validated in game.
- With those wider touches, the player drifted away from the ball: logged with `pes6r driftlog` (positions of the carrier and the ball every frame), a 45° touch leaves the ball at once while the run direction `+0x28` turns about 15° in half a second, and the gap grows from 100 to 500-700 until the next turning animation. A write breakpoint on the carrier's `+0x28` shows why: running with the ball, `0x497140` turns the facing towards its target by at most `0x100` (1.4°) per frame (`push 100h` at `0x4C2088`, then `+0x26`, `+0x28`, `+0x42` are written; `add ecx,100h` at `0x4C25C9` in another path). **The player turns faster with the ball** raises both steps (22° per frame by default). Tested in memory at 4, 8 and 16 times the game's: 16 felt best. Another with-ball path turns by `0x300` (`push 300h` at `0x4C214A`); it is left as it is.
- Tried and dropped: raising the `push 2Dh` (45°) of other strides (`0x4E1AAF`, `0x4E2231`, `0x4E2BF1`, `0x4E2D70`: they never run while dribbling); raising the 15° and 25° steps (no effect: the protected code ignores them); forcing the player's direction to the stick's (`0x4E2A7E`: the body turns, the ball does not) or to the touch's (`0x4E2B04`: the animation jumps, and with the ball change the player stops turning).

### Stick dead zone

Measured with `pes6r turnlog` while dribbling right and nudging the stick up and down, then with write breakpoints:

- The stick angle (`0x1016680` + pad × 4) never takes a value within about 16° of 0°, 90°, 180° or 270°: it stays exactly on the axis, then jumps to 16-17°. So the player keeps running along a line, whatever the free direction code does with the angle.
- The angle is written at `0x4B23E7` from 4 axis bytes per pad (`0x3BCEE51`…, one byte per half axis), through an arctangent; there, both axes under `0x19` give the centre dead zone.
- Those bytes come from the pad reader `0x8B7D63`…: each raw axis (0-255, centre 128) is zeroed when it is at most `0x25` (37) from the centre (`cmp eax,25h` at `0x8B7D83`, `0x8B7DAA`, `0x8B7DD1`, `0x8B7DFC`), without rescaling. That is a per-axis dead zone of 29 %, hence the 16° (arctan(37/127)).
- **Smaller analog stick dead zone** lowers the 4 limits (18 % = 23 by default). Validated in game: a slight move of the stick steers the player. 6 % (8) first, then raised to 18 % after matches: at 6 % the stick felt too sensitive (10 % and 14 % tried too). It applies to both sticks (right stick: manual passes).
- Set piece aims (#20): the corner wait (`0x58EFE1`, state 47) and the free kick wait (`0x58AEF6`) turn the aim `[player+0xA0]` by `0x40` per frame while the stick's right (`0x80`) or left (`0x20`) bit of `[player+0xB0]` is set, then keep it within `0xD00` of the facing (`0x4970F0`). The push is ignored, so with the smaller dead zone a slight touch turned it at full speed. A hook ignores a magnitude (`[player+0x101]`, 0-8, copied from the stick table by `0x4B2C02` / `0x4B2DE0`) under 3 and turns by the magnitude squared. Validated in game: floors 2, 3 and 4 tried, 3 best; squared felt better than linear. The goal kick wait (state 45, `0x598640`) has the same code twice (`0x5986FC`, `0x598B5B`) with a step of `0x80` and a range of `0x1000`: hooked the same way, the square doubled. Validated in game. Another copy in the protected section (`0x4483609`) turns the aim with buttons (bits `0x400` / `0x800`), not the stick. The stick table only holds the analog stick (writer `0x4B2300` reads the axes; its centre zone clears the bits too), so a magnitude of 0 with a direction bit set is the D-pad: the hooks keep the full step then. Training free kick ball placement (`0x4AF58E`): the position words `0x3BCE926` / `0x3BCE928` (copied to the ball at `0x4AF6B0`) move by `0x40` per frame for each direction bit (`0x80` −x, `0x20` +x, `0x10` −z, `0x40` +z), from `edi` = −0x40 and two `add word [..],0x40`. A hook sets `edi` to minus the step of the most pushed stick of the 8 pads, and the two adds become `sub word [..],di`. Validated in game. The player's copy of the stick angle (`+0xFE`) is only read at the kick (`0x5E58E2`). Found with read breakpoints on 4 kicker fields at once; `0x3BE0014`, found by a memory scan, was an animation frame.
- Tried before, without effect while the angle was flattened: removing the 22.5° rounding at `0x4E2AF3` (sprint touch) and `0x4DCE5D` (running stride: the stick angle), and lowering the stride dead zones (`0x4E2870`: 8, `0x4DCE73`: `0x18`, `0x4DCEEE`: `0x0E`, in 1/256 of a turn).
- Detaching the debugger (`pes6watch.ps1`) from the game closed it.

### Lofted passes and crosses

Kick probe with tags (with beeps, numpad), then breakpoints and reading:

- A **lofted pass** (circle in midfield) is kicked from `0x56A585` with the passer in state 8; a **cross** (circle from a wing or near the box) from `0x587C90` in state 9. Another state-8 kick comes from `0x50A420`.
- The lofted pass is decided by `0x5697B0` (called from the state handler around `0x56DA00`): the **receiver** is the passer's byte `+0x48`, the **point aimed at** `+0x9C` / `+0xA0`, the kind `+0x4C`. `0x56A260`… then computes the kick: speed from the kick power stat (`0x3BCF5B7`) and the height, capped by animation.
- The receiver is chosen by **`0x569020`** (passer, stick direction from `0x4B2F70`, which reads the analog stick's precise angle): the **pass gauge** `+0x4D` (0 to `0x80`, capped at `0x569821`) gives a **distance wanted** `0x800 + gauge / 8 × 0x380` (at least `0xE00`); a team-mate is kept only within `0x700` of that distance and, for a human, within **45°** of the stick (`cmp word [esp+0x1C],0x20` at `0x56938F`); the one with the smallest (angle gap + 1) × distance gap wins. With nobody kept, the pass goes along the stick with the gauge's power. Just before the kick, `0x45FFE0` re-checks the receiver in protected code.
- **Lofted passes: the press sets the distance, the stick the direction** makes the distance wanted grow with the square of the gauge (`0x800 + gauge² × 7 / 8`: the same maximum, about 5600 instead of 9200 at half a gauge), and narrows the cone to **22.5°**. Validated in game.
- **Through passes**: the pass point (passer `+0x9C` / `+0xA0`, found with a write breakpoint during a through pass) is placed at `0x4D0AF8`–`0x4D0B35`: start point + direction × **lead** (passer `+0xA8`), the lead being built from the angle and the receiver's run (`0x4D0AC4`). The ball reached the right receiver but far ahead of him, and out of play on the wings: the touchline guard at `0x4D0B83` (pulls the point 1024 back inside) only runs for some kinds and by chance from a passer's stat. **Through passes less far ahead of the receiver** scales the lead where it is read (60 % by default, F9). Validated in free training and in a match.
- Dropped: scaling the speed of the state 8 and 9 kicks (70 %, F9). The ball fell short of the receiver the game had chosen, so the aim looked random, and crosses (state 9) went wrong too.

### Pressing and challenges

Measured with a logger of the human-controlled player (buttons held `+0xB0`) against the carrier, and execute or write breakpoints:

- Pressing with X puts the player in state 15. The pressing code (`0x572A90`…) searches an **interception point** on the ball's predicted path, stores it in `[ball+0x1454]` / `[ball+0x145C]` (`0x572B55`) and runs towards it (direction at `0x572B8A`, passed to the movement functions; `0x49030F` turns `+0x26` by at most 22.5° a frame; the position moves along `+0x28` at `0x496D10`). The run is precise, not rounded to 8 directions.
- That run gets the player beside the carrier, mostly 340 to 500 units away. Aiming at the carrier instead made him trail behind (mean distance 2445 instead of 440, never under 300): the game's run is the right one.
- The **take-ball attempt** `0x461DC0` (player, distance, `0x50`) is only asked for within 160 units of the ball (`cmp edi,0A0h` at `0x572AE8`), and the attempt itself, for most animations, only goes on within speed / 128 + 2 × `0x50` (about 210 when running). So the presser almost never tried: 1 attempt in 15 s of opposing possession; 17 requests, 1 accepted, once the reach was raised alone.
- **Pressing players challenge for the ball from further** scales both (180 % by default: reach 288, parameter 127, the most a signed byte holds). Measured over 40 s: the presser now comes within 300 of the carrier (closest 116 instead of 337). Both teams are concerned.
- Not the cause: the players spread wider option (the presser is not in state 7), checked with the spread switched off in memory.

### Defensive line

Measured with a logger of the 22 players (positions, mode, the line word) and read in Ghidra (the exe imported without analysis, functions decompiled from their entry):

- Without the ball, the back four sat on the edge of their box (about 3700 from the goal line) with the ball just inside their half, and never reached the halfway line with the ball in the opponent's last third.
- The positioning dispatcher `0x528580` switches on the player's mode `+0x1DD` (`0x24` → `0x60CB40`, `0x27` → `0x60CD50`, marking, `8` → `0x619C80`…). Each routine builds a base target in the globals `0x3BCDCBC` (x) / `0x3BCDCC0` (z), with the side's direction in `0x3BCDCC4` (`0x642A90` when defending, `0x642540` when attacking), adjusts it and calls the target setter `0x4C9DC0`.
- The **defensive line** is a word per side, `0x3BCDCAE` + side × 2 (side = player `+0x13`), computed by `0x5E1BD0` from the smoothed ball x (`0x3BCDCB2`): ball in the other half → half its distance to the centre − `0xA00` (`sub eax,0A00h` at `0x5E1FC5`), kept 4096 behind the ball; ball in the side's own half → −`0xA00` − half that distance (`mov edx,0FFFFF600h` at `0x5E1E66`), kept 4096 − distance / 4 behind the ball (768 at least). Then the attack/defence level `[0x3BE0A6E + team]` (0 to 4): −3072, −1024, 0, +512, +1024; then −2000 (`0xB8C10C`); then −(100 − v) × 40, v being a team value from 0 to 100 returned by `0x531DA0` (a jump into the protection; about 30 for the team measured). The result is clamped to ±11929.
- **Higher defensive line** lowers the two `0xA00`. Zeroed in memory during a match, the line word rose by about 2000 (ball in the side's own half near the centre: −7503 → −5445 from the centre; just inside the other half: −5401 → −3501), and the back four by 0 to 1500 depending on the zone (75 s before, 75 s after): they already stood about 1400 above the word, which only acts as a floor (`0x60CB40`, and the end of `0x642A90`). Felt better in a match.
- A player's `+0x12` is not a stable team number (it changed between two snapshots of the same match); ids 1–11 and 12–22 are the two teams. The role is `+0x10` (1–7 defenders, 8–`0x17` midfielders, `0x18` and above forwards).
- An execute breakpoint held 12 seconds on `0x4C9DC0` was followed, two minutes later, by a crash of the game in the graphics driver. The game crashed the same way (`atidxx32.dll`, access violation) the next day within a minute of starting, in the menus, while only plain memory reads were running: the driver crashes it by itself, not the debugger.

### Forward runs in attack

Read in Ghidra and measured in CPU-vs-CPU matches nobody plays (`pes6r ab` with `pes6r positions`, then `pes6r placement`), 2026-11-16:

- While their side attacks (the carrier in the opponents' half), the team-mates are mostly in **mode 8** (`0x619C80`): defenders 65 %, midfielders 33 %, forwards 49 % of the time, 24 to 39 m from the carrier. Mode 8 sends them to their formation place (`0x642540`), kept a few metres behind the opponents' last line (`0x3BE0A10` + other side × 4: 256 to 1024 units by the player's type, less with a high attack stat), and `0x5EB4C0` keeps the target `0x800` (about 9 m) across from the nearest team-mate and from the carrier. Modes 1, 2 and 3 are the dead-ball states (`0x3BE0AA4`), not open play.
- A player leaves a run through `0x6134C0` (it goes on at `0x6134CD` through the protection): it frees his run slot and puts him back in mode 8 (his side has the ball, `0x3BE09E7` + team = 0) or `0x24`. The setter is `0x4CA9D0` (player, mode).
- **The runs have one slot each**: mode 9 (`0x3BCDCA1`) and `0x0A` (`0x3BCDCA2`) are the full-backs' overlaps, one per flank, chosen by `0x61E820`; mode `0x0F`, the run beyond the opponents' line (`0x61A100`), has one byte per side (`0x3BCDCA9` + side), chosen by `0x61DAC0`. So at most one player per team makes that run at a time.
- `0x61DAC0` picks the mode `0x0F` runner like this: nothing while `0x496C20`'s count reaches max(attack level, 2) (`0x61DB2D`; attack level `0x3BE0A6E` + team); a carrier who is a defender or a defensive midfielder may be picked himself (he runs on after his pass); otherwise the team-mates in mode 8, on the ball's flank (or one time in two), whose formation arrow `+0x1DC` allows it, by role (table at `0x61E62C`): centre-backs only at attack level 4, full-backs from level 2, defensive midfielders from level 3 and never when the carrier is one too, the other midfielders (roles 15 to 23) on a stat test (`+0x1A`, response) at any level; **the forwards (roles 24 and over) are never picked**. A last dice roll (1 in 10, 8 or 4, or none, by the attack and response stats, `0x61E460`) ends it. `0x483EF0` is the random number below its argument, `0x483E10` (stat, floor) rescales a stat above a floor to 0–100.
- Dropped: `runs-more` (the count cap raised to 4 and the last dice roll taken off). A/B over 4 × 4 minutes each: midfielders in mode `0x0F` while their side attacks 8 % → 7 %, defenders 3 % → under 3 %; releases kept by a team-mate 85 % → 81 %. Those two brakes are not what limits the runs. With 8 % for each of 4 or 5 midfielders and 3 % for each defender, the single slot is already taken close to half of the attacking time: more runs need a second runner (the slot byte is read in about ten places) or runs for the forwards, not easier picks.
- The same matches as they are (no patch): 1.4 team-mates within 15 m of the carrier, 2.6 ahead of the ball, 85 % of the releases kept, a run turned 0.25 s (real time, game at ×2) after the ball changed side, 17 to 20 % of the moving off-ball players facing 90° or more from their run.
- The other off-ball placement modes of state 7 (the logs print the mode in decimal: 36 = `0x24`, 39 = `0x27`, 29 = `0x1D`, 53 = `0x35`), read in Ghidra 2026-11-16: **7** (`0x615440`) is the general hold-your-place mode, one routine for both phases: sub-mode `+0x1DE` 2/3 when his side defends (`base_target_defending`, a defender (role under 10) pushed back by a constant, the others clamped to the opponents' offside line), 4/5 when it attacks (`base_target_attacking`, roles above 9 pushed forward, kept under the opponents' offside line), 6 = the carrier himself (leaves the mode and aims at where he stands); 2/4 while far from the target, 3/5 once near it. **`0x1D`** (`0x616B10`) is an attacking placement in fixed lanes (`base_target_attacking`, then x kept near the opponents' offside line and z set to a lane constant by role: roles 1-2 and the others in front of a threshold lane 2, roles 6-7 lane 3, role 8 lane 4, role 9 lane 5, sub-mode 6 = centred, z 0); it leaves as soon as his side no longer has the ball. **`0x35`** (`0x60B530`) is a single-slot cover role: only the player whose id is in `0x3BCDC45` runs it; he stands on the defence line word (+ the offside line) and his z follows the ball (z 0 when the ball is far to the side), then leaves (a sweeper-like cover). What starts modes 7, `0x1D` and `0x35` (the callers of the setter `0x4CA9D0`) is not read yet.

### Off-ball pace against real matches

`pes6r compare` on the CPU matches of 2026-11-16: the off-ball players go at a median 3.3 to 4.0 m/s against 2.0 to 2.1 in the two real matches, about half of them over 4 m/s against 10 to 15 %, and 9 to 14 % walk (1–2 m/s) against 30 to 37 %; 8 % of the runs turn back within a second against none. There are already more runs in behind at once (0.6 to 0.8) than in the real matches (0.2 to 0.4).

- The running speed `+0x1C` is about 1100 units per m/s (8000: 7.1 m/s). It is written by `0x48FEF0` (steps towards a speed in EBX: `0x49001C`…) and, most often, by the code at `0x4D34F5`…`0x4D3618`, which holds the speed aimed at in EDI and either sets it (`0x4D358A`) or steps towards it (`0x4D35FD`, `0x4D3612`).
- `pace08`, first version: `+0x1C` capped at `0x600` + |dx| + |dz| to the target in `mode08_positioning` (`0x619D77`). No real effect (fast players of the side with the ball 24 % → 18 %, median 3.3 → 3.1 m/s): half of the mode 8 samples stayed over the cap, the movement code writes the speed again afterwards.
- `pace08`, second version: the same cap on EDI. A hook at `0x4D34F3` crashed the game twice (`0x4D34D0` jumps to `0x4D34F5`, in the middle of the hook; and a wrong return address the first time). The hook is now at `0x4D34F5`; it ran 45 s in a match without a crash, **but it has not been measured**. Before hooking, look for the jumps that land inside the bytes replaced.
- `pace08`, measured (France against England, 3 × 240 s, `startmatch` then `ab`): no effect, off-ball median 3.4 m/s without the patch, 4.1 with it (real matches 2.0). It only covered mode 8 (about 17 % of the off-ball samples; state 7 holds modes 8, 36, 39, 7, 29, 53 for about 52 %) and its cap let the whole distance to the target add to it. The A/B ended with a game crash (`0x80000003` at `0x5E2920`): `PatchSet` wrote the 0xCC of the cave back before the jump into it; it now reverts in the reverse order of the list (cave first in `patches.json`, jump last). `pace09` (all state-7 modes, cap `0x600` + distance / 4) was the next test, see below.
- The game starts on its language screen at every launch (the user always had it): `pes6r startmatch` chooses Français, goes through the menus and starts a CPU-vs-CPU match, France against England by default (the teams of the first group, Europe A, are mapped; the other groups are not yet). Use top teams for every unattended match: the complaints are about good players.
- **Move_speed_towards `0x48FEF0`** (player, target speed, step; cdecl; 82 callers in the action code `0x490000`-`0x4C3E00`) is the other writer of `+0x1C` (the body is in the protection, `jmp [0x44603DC]` to an obfuscated stub at `0x44B6A20`, not decoded). `pace09` (all state-7 modes, cap `0x600` + distance / 4 on EDI at `0x4D34F5`) turned jogging into walking but left the sprints (23 -> 24 %). `pace10` adds a cap on the target-speed argument at the entry of `0x48FEF0` (cave `0x5E2980`): off-ball median 3.2 -> 1.9 m/s (real 2.0), sprints 23 -> 1 % (real 2-3 %), walking 10 -> 32-35 % (real 30-37 %), challenges won 1.8 -> 0.8 per minute (real 0.7-0.8). Too strong: running 1 % (real 7-9 %), 22 releases per minute (real 34-40), runs turned within 4 s 55-64 % (real 68-71 %). To soften (distance / 2 or / 3).
- **Restart state** `0x3BE0AA4` (`GameLayout.RestartState`): 0 in play, 1 stoppage time announced, 2 free kick, 3 corner (most likely), 5 the goalkeeper holds the ball, 6 throw-in, 7 the long breaks with no HUD: a goal sequence, half-time (the "Résumé" of the goals loops until Esc then Enter) and full time. `pes6r ab` used to press Esc in any freeze of 4 s, which opened the PAUSE menu at corners; it now presses only after 4 s at 7, or after 15 s of real standstill at 0, and never at 1 to 6 (verified 2026-11-16: 7 groups of keys, all inside a 7). The `lost` flag (keepers not near their goals) can hold during play, so it only counts at 7.

### Test keys

F2 to F9 change a value during a match and show it through Kitserver, like F1. Their set-up is called from the kick function's entry until done; their state is in the 32 bytes before the close dribbling ones (`0xBC3FD0`). F2 to F5 step by 10 %, F6 by 25 %, F7 by 15°, F8 by 20 %, F9 by 10 % (the texts for every step sit in caves of their own).

---

## 4. Memory map (reference build)

| Data | Address / field |
|---|---|
| Players | `0x3BDC980` + id × `0x240` (id 1–22 on the pitch) |
| Player: team, slot (0 = keeper) | `+0x12`, `+0x11` |
| Player: state, timer | `+0x16`, `+0x18` |
| Player: running speed, maximum speed | `+0x1C`, `+0x58` |
| Player: heading, direction asked | `+0x2C`, `+0x2E` |
| Player: target, position | `+0x6C` / `+0x70`, `+0xE0` / `+0xE8` |
| Player: animation | `[+4]` (`+0x2D` foot, `+0x30` animation, `+0x34` frame) |
| Player: buttons held, pressed | `+0xB0`, `+0xB4` |
| Ball carrier id | `0x3BE09CC` |
| Ball | `[0xBCCE94]`: position `+0x10/+0x14/+0x18`, vertical speed `+0x30`, speed `+0x50`, direction `+0x58`, spin `+0x6A/+0x6C/+0x74` |
| Stats | `0x3BCF5A8` + (team × 32 + slot) × `0x348`, 7 bits per stat (checked against the stats shown in game, 2026-11-15): `+0x0` bit 0 favoured foot, then from `+0x2` in the order of the game's player screen: attack, defence, balance, stamina, top speed (`+0x6`), acceleration (`+0x7`), response, agility, dribble accuracy (`+0xA`), dribble speed, short pass accuracy and speed, long pass accuracy and speed, shot accuracy (`+0x10`), shot power, shot technique (`+0x12`), free kick accuracy, swerve, heading, jump (`+0x16`), a stat not shown (`+0x17`), technique (`+0x18`), aggression (`+0x19`) |
| Analog sticks | `0x1016680` + pad × 4: angle (word), magnitude, direction bits |
| Animation (`[player+4]`) | `+0x28` playback rate (float, set from the global frame step, or as a % by `0x65D9B0`), `+0x30` animation, `+0x34` frame count, `+0x38` time (float); advanced by `0x65D870`, set by `0x65D7D0` |
| Ball physics constants | 10 floats at `0xB8AE68` (see below) |

Ball physics constants (the 40 bytes changed by Rechi's BallPhysics Editor and DonMike's `gameplay.dll`), read by the ball flight code `0x475810`:

| Address | Value | Role | Read elsewhere |
|---|---|---|---|
| `0xB8AE68` | 1/600 | damping of a slow rolling ball | 1 place |
| `0xB8AE6C` | 1/32 | rolling drag | 13 places |
| `0xB8AE70` | 188 | gravity, added to the vertical speed every frame | 3 places, among them the kick aiming (`0x515909`) |
| `0xB8AE74` | 4.07e-5 | spin lift (Magnus effect): spin × speed² | no |
| `0xB8AE78` | −376 | below this, the ball stops bouncing and rolls | no |
| `0xB8AE7C` / `0xB8AE80` / `0xB8AE84` | 184 / −1/256 / 9024 | bounce: below 9024, 53 to 63 % of the vertical speed is kept (from the ball's `+0x50`); above, 72 % of the excess | few |
| `0xB8AE88` | 0.8 | horizontal speed lost on a bounce | 49 places |
| `0xB8AE8C` | −28 | ground height (ball radius) | 11 places |

---

## 5. Research tools

The scripts in [`scripts`](scripts) were used to find the fixes. The app does not need them: the patch itself is in `src/PES6Patcher.Core`.

Most of them now live in a C# console tool, [`Pes6Research`](Pes6Research) (`pes6r.exe`): one shared memory module instead of a `ReadProcessMemory` copy per script, no compile on every run, and steadier sampling (100 ms asked gives 100 ms, not 110-125). Build it once with `dotnet build research/Pes6Research -c Release`, then run `research/Pes6Research/bin/pes6r.exe <command>` (no command: the list). The options keep the scripts' names. The parts that need neither the game nor its files (options, hex, patches on a fake memory, exe sections, depth and shape) are tested: `dotnet test research/Pes6Research.Tests`. The commands, and the script each replaces:

| Command | Was | Purpose |
|---|---|---|
| `pes6r dump -Address 0x3BECC60 [-Length 0x40]` | `pes6dump.ps1` | memory as int16 / int32 / float columns (decimal point, whatever the Windows language) |
| `pes6r positions [-Seconds 60] [-Out positions.csv] [-Every 100] [-StartDelay 5] [-Patch name]` | `pes6positions.ps1` | CSV of the 22 players and the ball, with each player's role (`+0x10`), placement mode (`+0x1DD`), facing, run and aimed direction, and the ball's height (`by`, growing downwards: about -23 on the ground); `-Patch` adds a column A/B telling whether that patch is on (to run beside `ab`); written as it goes, so Ctrl+C or the game crashing keeps the samples; no line when the players cannot be read |
| `pes6r patch [-List] [-Apply a,b] [-Revert a,b] [-RevertAll] [-Force]` | `pes6patch.ps1` | named memory patches of `patches.json`, see below; `pes6r ab` and `pes6r scoreprobe` use it |
| `pes6r code -Address 0x8744F0 [-Length 16] [-Hex "1C 03 00 00"]` | `pes6code.ps1` | read bytes, or write them (memory only, code included) and read them back |
| `pes6r players` | `pes6players.ps1` | one snapshot of the 22 players and the ball |
| `pes6r depth -In positions.csv [-Field p\|t]` | `pes6depth.ps1` | back line by ball zone from a `positions` CSV (`t`: the players' targets); writes `<in>-<field>-teams.csv` with a decimal point |
| `pes6r placement -In positions.csv` | (new, 2026-11-15) | off-ball placement of a `positions` log: each player's place, role and modes; crosses (team-mates and strikers at the landing); the crowd around the carrier and the balls won by a challenge; attack support (team-mates near, ahead, running forward), releases kept by a team-mate, reactions after the ball changes side; facing against run; target jumps by mode; the defensive midfielder against the back four. Split by phase when the log has the `patch` column. The defenders, midfielders and forwards of the attacking side are also given by placement mode (share, distance to the carrier, running forward). Log at `-Every 50`: the moves are measured over 5 samples |
| `pes6r compare -In a.csv,b.csv [-Speed 2] [-Metrica folder] [-Games 1,2] [-SaveReference]` | (new, 2026-11-16) | `positions` logs against real matches, the same measures in metres and seconds of match time, one column each (and one per phase A/B when the log has the `patch` column): off-ball speeds, attack support, the crowd around the player on the ball, team shape, passing, challenges, reactions when the ball changes side, crosses. The real columns are read from [`real-matches.csv`](real-matches.csv) (two matches of the [Metrica Sports sample data](https://github.com/metrica-sports/sample-data), tracking at 25 Hz and events); `-Metrica` computes them again from the downloaded files and `-SaveReference` writes them back. First reading (CPU match, 2026-11-16): the game's players move at a median 3.5-4.2 m/s off the ball against 2.0 in the real matches (above 5.5 m/s 24-30 % of the time against 2-4 %), 6-8 % of their runs turn back within a second against 0 %, they react to a change of side in 0.4-0.6 s against 1.5 s, 2.0-2.6 team-mates are ahead of the ball against 3.6-3.8, and there are 19-30 releases per minute of possession against 34-40, with the same share reaching a team-mate (77-85 %) |
| `pes6r findref -Exe PES6.exe.orig -Values 0x3BCDCAE,0x3BE0A6E` | `pes6findref.ps1` | on the exe file: the places holding a 32-bit value (0.1 s instead of 3.6) |
| `pes6r readva -Exe PES6.exe.orig -Addresses 0xB8C10C,0x5E23FC [-Length 8]` | `pes6readva.ps1` | on the exe file: float, int32 and bytes at virtual addresses |
| `pes6r shape [-Seconds 90] [-Every 250] [-Out shape.csv]` | `pes6shape.ps1` | team shape out of possession by ball zone |
| `pes6r driftlog [-Seconds 30] [-Out drift.csv] [-StartDelay 3]` | `pes6driftlog.ps1` | the carrier and the ball every ~5 ms while the carrier moves |
| `pes6r turnlog [-Seconds 30] [-Out turn.csv] [-StartDelay 5]` | `pes6turnlog.ps1` | one line per change of the carrier's fields; sleeps 1 ms between reads instead of spinning a core, and still catches every frame |
| `pes6r heading -Players 0x3BDD700,0x3BDDB80 [-Seconds 10]` | `pes6heading.ps1` | the players' heading against the stick angle; 10 samples a second, as asked (the script took 9) |
| `pes6r stick [-StartDelay 5] [-Hold 3] [-Out stick.txt]` | `pes6stick.ps1` | memory that follows the stick, guided by beeps |
| `pes6r ab -Patch defence-line [-Phase 90] [-Cycles 3] [-Settle 10] [-SpeedPatch speed-x2] [-SkipScan 1]` | `pes6ab.ps1` | automatic A/B of a patch in a CPU-vs-CPU match at x2 speed: back line by ball zone, patch off (A) and on (B); at the end (Ctrl+C too) the game is put back exactly as it was, so a patch the patcher applied in the exe stays applied, and a line word the keeper's side byte cannot index gives NaN instead of stopping the run |
| `pes6r scoreprobe [-Seconds 600] [-SkipScan 1]` | `pes6scoreprobe.ps1` | 32-bit values of a region that only ever go up by 1, with the ball's position (the search for the score counters) |
| `pes6r calls -Address 0x4C9DC0 [-Seconds 10] [-Out calls.csv] [-NArgs 3]` | `pes6calls.ps1` | debugger: every call of a function, with the player, the arguments and the callers |
| `pes6r hits -Addresses 0x404338,0x4043A3 [-Seconds 3]` | `pes6hits.ps1` | debugger: how many times up to 4 addresses run; callers, EDI and first argument of the first |
| `pes6r watch -Address 0x3BECC84 [-Seconds 4] [-Read]` | `pes6watch.ps1` | debugger: the instructions that write (or with `-Read` use) an address |
| `pes6r viewport [-Seconds 3]` | `pes6viewport.ps1` | debugger: the Direct3D `SetViewport` calls |
| `pes6r window` | `pes6window.ps1` | geometry and DPI handling of the game's windows, in physical pixels |
| `pes6r capture [-Out shot.png]` | `capture-window.ps1` | screenshot of the main window, even behind others; `-Process PES6Patcher` for the app |

The four debugger commands share one module (they were four copies of it), detach on Ctrl+C too, and stop when the game closes. Checked against the scripts in a live match on 2026-11-14 (same instructions, callers and columns; the game kept running after 8 short sessions). Keep the sessions short.

`ab` and `scoreprobe` send the skip key (Esc, the user's Start) only when the game window really came to the front, so it never lands in another window.

The commands guided by beeps (high: start, low: end) stop cleanly on Ctrl+C and when the game closes, and keep what they logged.

Every command takes `-Process <name>` to read another process than PES6.

| Script | Purpose |
|---|---|
| `DumpAt.java`, `DumpRefs.java`, `ListRefs.java`, `Disasm.java`, `FindDevCalls.java` | Ghidra scripts (headless mode): decompile a function, list the references to an address, disassemble a range, find the calls to a Direct3D 8 device method |
| `pes6r code`, `pes6r dump` | read or change bytes in the running game (memory only) |
| `pes6r patch`, `patches.json` | named memory patches in the running game: `-List`, `-Apply name`, `-Revert name`, `-RevertAll`; the original bytes are checked first, so nothing is written over another patch or build. Add an entry to `patches.json` for every idea tried in memory |
| `pes6r players`, `pes6r positions` | read-only (ReadProcessMemory, no debugger): a snapshot of the 22 players and the ball, or a CSV of them every 100-250 ms for a whole match |
| `pes6r depth`, `pes6r shape` | from that CSV (or sampling live): height of the back line by ball zone, and the team shape (defenders, midfielders, forwards, length, width) while the opponent has the ball |
| `pes6r calls` | execute breakpoint on a function: every call with its caller and arguments. It attaches the debugger (a trace cave in the exe padding, read with ReadProcessMemory, would avoid it; the game also crashes by itself in the graphics driver, see the note on the defensive line) |
| `pes6r findref`, `pes6r readva` | work on the exe file (the game need not run): find the code holding an address, read constants at virtual addresses |
| `pes6ghidra.ps1`, `ApplyLabels.java`, `DecompIn.java`, `DecompAt.java`, `../ghidra-labels.csv` | a persistent Ghidra project of the original exe, in a short path (`-Import` once, about 40 s; `-Decompile 5E1BD0,642A90` about 12 s). The functions created and named are saved in it; add what is learned to `ghidra-labels.csv` and run `-Labels`. `-At` makes the function exactly at the address given (code reached by a jump out of the protection: a function that starts with `jmp [0x44xxxxxx]` goes on where the running game's pointer leads, `pes6r code -Address 0x44xxxxxx`). Quote the addresses that look like numbers (`'61E932'` is infinity for PowerShell). The decompiler drops the branches of some jump tables: check with `Disasm.java` (start, end), which also disassembles what is not yet |
| `pes6r watch` | hardware write or read breakpoint on an address: which instructions touch it |
| `pes6r hits` | hardware execute breakpoints: how many times some code runs, with which callers and arguments |
| `pes6r viewport` | records the `SetViewport` calls (rectangle and caller), which led to the list clipping |
| `pes6r stick` | memory capture guided by beeps (stick at rest, right, up), which led to the controller state |
| `pes6r heading` | measures the players' real heading against the stick angle, with and without the free direction |
| `pes6r turnlog` | beep-guided log of the ball carrier (state, animation, frame, headings, speed, ball direction): one CSV line per change, which showed the sprint turn steps |
| `pes6r window`, `pes6r capture` | geometry and screenshot of the game's or the app's window |
| `make-signatures.ps1` | builds a location's signature: the surrounding bytes, with absolute addresses and call targets masked, extended until they appear only once in the code |
| `StringRefs.java` | Ghidra script: decompiles the functions that use a string (for example the Kitserver version names in `kload.dll`) |
| `set-resx.ps1` | adds, changes or removes texts in the English and French resources |

The gameplay options were mostly found with variants of `pes6r watch` and `pes6r hits` (several addresses at once, registers and stack logged at each hit), and with memory captures of the players, the ball and the stats sampled while playing, then compared.

### Several builds of the exe

Kitserver tells three game versions apart: "PES6 PC" (1.00), "PES6 PC 1.10" and "WE-PES 2007 PC". Community patches also ship their own exes. Rather than an address table per build, which would require owning every exe, the app finds the locations by signature (`src/PES6Patcher.Core/Patching/ExeLocator.cs`):

- the signatures come from `make-signatures.ps1`, built forwards from each location;
- the addresses used by the added code (constants, functions, tables, stats, player array…) are read from the code found, then checked;
- the two calls that pass the width to the 3D are recognised by the content of the called function, because a third identical call feeds the 2D clipping;
- the free areas are the runs of `CC` of at least 80 bytes in the code, and the state goes into the end of `.rdata`, which Windows loads even beyond the file's data;
- the maximum speed formula is searched in every executable section, as it lives in the protection's `.rld` section.

On the reference exe, this engine produces the v6 validated in game byte for byte (tests `Build_matches_the_exe_validated_in_game`).

Ghidra's decompiler does not start from a path longer than 260 characters: install Ghidra in a short folder.

---

## 6. Technical points of the widescreen setup

- **Borderless window.** `kload.cfg` sets the window with `dx.window.x/y/width/height` and `dx.window.style = 0x96000000`. It covers the screen exactly. The game must stay in windowed mode.
- **Game resolution.** `settings.exe` offers nothing above 1920 × 1080, so the resolution is written directly into `Documents\KONAMI\Pro Evolution Soccer 6\settings.dat`:
  - width at bytes 4-5;
  - height at bytes 6-7;
  - at bytes 2-3, a checksum: the 16-bit sum of the bytes from the 4th one on.

  Do not reopen `settings.exe` afterwards: it would put 1920 × 1080 back.
- **Windows scaling.** With a 125 % display, the game does get the physical resolution. The offset seen at first came from the 1920 × 1080 window and the game resolution, not from the scaling.
- **32:9 aspect ratio.** The menus only take about a third of the width, in the centre.

Profiles validated in game:

| Profile | 2D half-width | Render (kload) | Window | Game resolution | 3D aspect (lodmixer) |
|---|---|---|---|---|---|
| 16:9 | 398 | 2560 × 1440 | normal | 1920 × 1080 | 1.77778 |
| 32:9 | 796 | 5120 × 1440 | borderless, 5120 × 1440 at (0, 0) | 5120 × 1440 | 3.55556 |

---

## 7. Installing and going back

The PES6 Patcher app installs these settings for any resolution. Its **Restore original** button puts every file back as it was before its first use.

---

## 8. Other dead ends

- **Smoother menu font**: switching the font texture filtering to linear changed nothing visible.
- **AI 360° dribbling**: the AI's dribble directions are already rounded (mostly to 45°) when it decides them. `+0x2C` very likely holds the direction it aims at; using it only changed each touch by 2 to 13°, with no visible gain.
- **Less ball spin lift**: scaling the Magnus constant (`0xB8AE74`) from 10 to 150 % with a test key made no difference felt in game: that term is tiny next to the gravity.

---

## 9. Known limitations

- The menu backgrounds are only 512 wide: black bars on the sides.
- In the main menu, a second copy of the player picture shows in the right bar.
- The free direction follows the stick's precise angle when running and for the most frequent dribble touch; some less frequent touches and special moves keep the game's directions.
- Only the reference build has been tested in game; other builds are patched when their code matches.
