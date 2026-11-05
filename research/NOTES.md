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
- **Close dribbling**: at the relay's entry, the speed of the 7 dribble touches is scaled down (× 14/16 running, × 13/16 sprinting), in proportion to the player's dribble accuracy (stat `0x3BCF5C0`, the one the game reads for the touch direction noise): nothing up to 65, the full reduction from 95.
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
- **Human teams**: at every kick, for each pad whose stick is pushed, each team's active player gets +1 when its input direction is the stick's (or the opposite one), −2 otherwise. A team at 32 or more (out of 64) is human. The AI's shots are left alone.
- **Shot kinds**: a shot is player state 4. Its handler `0x5BBF90` switches on `+0x18` (`0x00`…`0x27`, table at `0x5BD858`, cases at `0x5BD800`); 17 values are shot kinds (`0x10`–`0x14`, `0x16`, `0x18`, `0x1B`–`0x1E`, `0x21`–`0x25`, `0x27`), each calling the shot function `0x5B9130` from its own site (`0x5BC321` … `0x5BD7BA`) with its own base power (`0x300` to `0xA00`) and parameters. The kind is chosen by the shot decision code, `0x544954` to `0x54980A` (19 places that set state 4 and `+0x18`), plus `0x579AEB` (kind `0x27`) and the set pieces (`0x58C026`, `0x58C323`). Which kind is which (running shot, volley, overhead kick…) was not mapped: read when a shot starts, `+0x18` held a phase (0 or 1), not the kind.
- The shot function draws many random numbers (`0x483EF0`, `0x483F40`, about 40 calls between `0x5B93CB` and `0x5B9E59`) before the kick call `0x5BBC9A`; that is why the accuracy acts on the kick call's final direction and height rather than on each draw.
- **Lobs**: a lob is a shot whose shooter holds button `0x400` (L1) in `+0xB0` when the ball leaves (all the lobs logged; none of the other shots). Kick probe (`pes6probe.ps1`, branch `kick-probe`): classic lobs use animations `0xC9`, `0xCA`, `0x244`, `0x245` and leave at a height of 45 to 68, against 13 to 35 for shots; badly placed lobs use other animations (`0xF`, `0xEE`) and leave low. The accuracy used to scale every height above 20 as an error, which flattened lobs (half of their height at 50 %, nearly all at 1 %). A lob now keeps 95 % of its height and gets the lob power (70 % by default) on its speed. Tried and dropped: telling lobs by their height (misses the low ones); scaling the power argument at the shot function's entry for lobs (irregular: the lob's strength is decided further on).
- Measured in game with the share at 1 %: shots go "much more on target"; what still misses is the aim itself (the direction asked), not the random gap.
- Dead end: the shot gauge. The power is decided by the shot kind and formulas (for example `0x108 + [player+0xA4] × 24 / (8142 − 16 × [player+0x5C])`), not by a visible counter. The human input is copied into the player (`+0xB0`, bit `0x800` = shoot) by protected code (around `0x448A73D`); the AI presses the same bit. `[player+4]+0x34` is the animation frame, not a press counter.

### Players spread wider

- Where a player places himself goes through the **target setter** `0x4C9DC0` (player, x, z), called by 45 pieces of the positioning AI; its body continues in protected code. Only the players placing themselves (state `+0x16` = 7) use it.
- The added code moves the target away from the centre of the team's targets (`+0x6C` / `+0x70` of its outfield players), across the pitch only (z). Spreading the length (x) too made the defenders drop close to their keeper.

### Speed gap

- The running speed is `[player+0x1C]`. Every frame, `0x48FEF0` moves it towards a target speed given by the caller (sprinting with the ball: `0x4C20F9`), with an acceleration time from stat `0x3BCF5B0`.
- The **maximum speed** `[player+0x58]` is computed, and recomputed during the match, at `0x447BF52` (`.rld` section of the protection, plain code): 7542 + 12 × (top speed − 40) × 100 / 60 − 16 × `[player+0x5C]`. The top speed stat is the byte `0x3BCF5AE` (correlation 1.00 with `+0x58` over 22 players). One stat point is only worth 20 units: 68 → 8094, 97 → 8682.
- The added code scales the stat's part around a player at 75. As the value is recomputed during the match, F6 applies at once.

### Sharp turns when sprinting

Measured with `pes6turnlog.ps1` (one line per change of the ball carrier's fields) and execute breakpoints:

- Running with the ball, a stride lasts about 26 frames (0.46 s, animations 65/66) with one touch near frame 7; the stick's new direction is only taken at the next stride. Sprinting, a stride lasts about 0.67 s (animations 29/30 = `0x1D`/`0x1E`). A turn of more than 112° goes through a turning animation (552, about 0.65 s).
- The sprint stride starts in `0x4E2830` (called from `0x5B7954` when the sprint button is held, `0x4DCBB0` otherwise). It compares the stick direction (`0x4B3010`) with the facing `+0x26`, in 1/256 of a turn: below 8 nothing happens, from `0x50` (112°) the turning animation is used. Otherwise:
  - the **player's direction** turns by a fixed step: animation `0x1E` with 15° (`push 0Fh` at `0x4E299D`) on the favoured foot's side, `0x1D` with 25° (`push 19h` at `0x4E2A51`) on the other, through `0x4F3820`, which goes into the protected code; both branches meet at `0x4E2A7E` with the new direction in `di`, written to `+0x2C` at `0x4E2FA7`;
  - the **ball's direction** is computed at `0x4E2AFF`: `0x4970F0` (turn a direction towards another by at most a given angle) from the facing rounded to 22.5° (`(+0x26 + 0x800) & 0xF000`) towards the stick, by at most `0x1000` (`push 1000h` at `0x4E2AE6`). Logged at the touch (relay call at `0x5AFB3B`, direction from `[player+0x30]`): `0xA000`, `0xB000`, `0xC000`… exactly one 22.5° step per touch, whatever the stick asked. **That step is what makes the turns go "by stages".**
- The player then runs after the ball: `0x5AFD45` points `+0x2C` every frame at the ball's predicted position.
- **Sharper turns when sprinting** replaces the `push 1000h` by the maximum turn of the carrier: 22.5° up to 65 in dribble accuracy (the game's), the chosen maximum (90° by default) from 95, in proportion between. A touch that turns the ball more than 22.5° from its current direction (`[ball+0x58]`) also turns the player's run (`+0x28`) and facing (`+0x26`, `+0x42`) to the touch direction, at both sprint touches: `0x5AFB32` (animation 29) and `0x5AFE5C` (animation 30, the other foot: `xor ecx,ecx ; mov cx,[edi+0x30] ; add esp,0Ch` before the relay call). Until 1.18 only the first was hooked, with a speed loss of up to 40 % instead: on the other foot's touch the player finished his stride straight and only turned at the next one, about 300 ms late. A slowdown of the ball and the player from 45° was tried (30 %, then 15 % at 90°) and dropped with the next point. Snapping the facing at the touch makes the animation jump, so the sprint stride now hands turns from 45° to the normal dribble stride (`cmp ax,50h` at `0x4E287C` → `20h`), which plays the turning animation: validated in game.
- With those wider touches, the player drifted away from the ball: logged with `pes6driftlog.ps1` (positions of the carrier and the ball every frame), a 45° touch leaves the ball at once while the run direction `+0x28` turns about 15° in half a second, and the gap grows from 100 to 500-700 until the next turning animation. A write breakpoint on the carrier's `+0x28` shows why: running with the ball, `0x497140` turns the facing towards its target by at most `0x100` (1.4°) per frame (`push 100h` at `0x4C2088`, then `+0x26`, `+0x28`, `+0x42` are written; `add ecx,100h` at `0x4C25C9` in another path). **The player turns faster with the ball** raises both steps (22° per frame by default). Tested in memory at 4, 8 and 16 times the game's: 16 felt best. Another with-ball path turns by `0x300` (`push 300h` at `0x4C214A`); it is left as it is.
- Tried and dropped: raising the `push 2Dh` (45°) of other strides (`0x4E1AAF`, `0x4E2231`, `0x4E2BF1`, `0x4E2D70`: they never run while dribbling); raising the 15° and 25° steps (no effect: the protected code ignores them); forcing the player's direction to the stick's (`0x4E2A7E`: the body turns, the ball does not) or to the touch's (`0x4E2B04`: the animation jumps, and with the ball change the player stops turning).

### Stick dead zone

Measured with `pes6turnlog.ps1` while dribbling right and nudging the stick up and down, then with write breakpoints:

- The stick angle (`0x1016680` + pad × 4) never takes a value within about 16° of 0°, 90°, 180° or 270°: it stays exactly on the axis, then jumps to 16-17°. So the player keeps running along a line, whatever the free direction code does with the angle.
- The angle is written at `0x4B23E7` from 4 axis bytes per pad (`0x3BCEE51`…, one byte per half axis), through an arctangent; there, both axes under `0x19` give the centre dead zone.
- Those bytes come from the pad reader `0x8B7D63`…: each raw axis (0-255, centre 128) is zeroed when it is at most `0x25` (37) from the centre (`cmp eax,25h` at `0x8B7D83`, `0x8B7DAA`, `0x8B7DD1`, `0x8B7DFC`), without rescaling. That is a per-axis dead zone of 29 %, hence the 16° (arctan(37/127)).
- **Smaller analog stick dead zone** lowers the 4 limits (6 % = 8 by default). Validated in game: a slight move of the stick steers the player. It applies to both sticks (right stick: manual passes).
- Set piece aims (#20): the corner wait (`0x58EFE1`, state 47) and the free kick wait (`0x58AEF6`) turn the aim `[player+0xA0]` by `0x40` per frame while the stick's right (`0x80`) or left (`0x20`) bit of `[player+0xB0]` is set, then keep it within `0xD00` of the facing (`0x4970F0`). The push is ignored, so with the smaller dead zone a slight touch turned it at full speed. A hook ignores a magnitude (`[player+0x101]`, 0-8, copied from the stick table by `0x4B2C02` / `0x4B2DE0`) under 3 and turns by the magnitude squared. Validated in game: floors 2, 3 and 4 tried, 3 best; squared felt better than linear. The goal kick wait (state 45, `0x598640`) has the same code twice (`0x5986FC`, `0x598B5B`) with a step of `0x80` and a range of `0x1000`: hooked the same way, the square doubled. Validated in game. Another copy in the protected section (`0x4483609`) turns the aim with buttons (bits `0x400` / `0x800`), not the stick. The player's copy of the stick angle (`+0xFE`) is only read at the kick (`0x5E58E2`). Found with read breakpoints on 4 kicker fields at once; `0x3BE0014`, found by a memory scan, was an animation frame.
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
| Stats | `0x3BCF5A8` + (team × 32 + slot) × `0x348`, 7 bits per stat: `+0x0` bit 0 favoured foot, `+0x6` top speed, `+0x8` (read for the acceleration), `+0x18` dribble accuracy |
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

| Script | Purpose |
|---|---|
| `DumpAt.java`, `DumpRefs.java`, `ListRefs.java`, `Disasm.java`, `FindDevCalls.java` | Ghidra scripts (headless mode): decompile a function, list the references to an address, disassemble a range, find the calls to a Direct3D 8 device method |
| `pes6code.ps1`, `pes6dump.ps1` | read or change bytes in the running game (memory only) |
| `pes6watch.ps1` | hardware write or read breakpoint on an address: which instructions touch it |
| `pes6hits.ps1` | hardware execute breakpoints: how many times some code runs, with which callers and arguments |
| `pes6viewport.ps1` | records the `SetViewport` calls (rectangle and caller), which led to the list clipping |
| `pes6stick.ps1` | memory capture guided by beeps (stick at rest, right, up), which led to the controller state |
| `pes6heading.ps1` | measures the players' real heading against the stick angle, with and without the free direction |
| `pes6turnlog.ps1` | beep-guided log of the ball carrier (state, animation, frame, headings, speed, ball direction): one CSV line per change, which showed the sprint turn steps |
| `pes6window.ps1`, `capture-window.ps1` | geometry and screenshot of the game's or the app's window |
| `make-signatures.ps1` | builds a location's signature: the surrounding bytes, with absolute addresses and call targets masked, extended until they appear only once in the code |
| `StringRefs.java` | Ghidra script: decompiles the functions that use a string (for example the Kitserver version names in `kload.dll`) |
| `set-resx.ps1` | adds, changes or removes texts in the English and French resources |

The gameplay options were mostly found with variants of `pes6watch.ps1` and `pes6hits.ps1` (several addresses at once, registers and stack logged at each hit), and with memory captures of the players, the ball and the stats sampled while playing, then compared.

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
