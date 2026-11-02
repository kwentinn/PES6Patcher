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
- Dead end: the shot gauge. The power is decided by the shot kind and formulas (for example `0x108 + [player+0xA4] × 24 / (8142 − 16 × [player+0x5C])`), not by a visible counter. The human input is copied into the player (`+0xB0`, bit `0x800` = shoot) by protected code (around `0x448A73D`); the AI presses the same bit. `[player+4]+0x34` is the animation frame, not a press counter.

### Players spread wider

- Where a player places himself goes through the **target setter** `0x4C9DC0` (player, x, z), called by 45 pieces of the positioning AI; its body continues in protected code. Only the players placing themselves (state `+0x16` = 7) use it.
- The added code moves the target away from the centre of the team's targets (`+0x6C` / `+0x70` of its outfield players), across the pitch only (z). Spreading the length (x) too made the defenders drop close to their keeper.

### Speed gap

- The running speed is `[player+0x1C]`. Every frame, `0x48FEF0` moves it towards a target speed given by the caller (sprinting with the ball: `0x4C20F9`), with an acceleration time from stat `0x3BCF5B0`.
- The **maximum speed** `[player+0x58]` is computed, and recomputed during the match, at `0x447BF52` (`.rld` section of the protection, plain code): 7542 + 12 × (top speed − 40) × 100 / 60 − 16 × `[player+0x5C]`. The top speed stat is the byte `0x3BCF5AE` (correlation 1.00 with `+0x58` over 22 players). One stat point is only worth 20 units: 68 → 8094, 97 → 8682.
- The added code scales the stat's part around a player at 75. As the value is recomputed during the match, F6 applies at once.

### Test keys

F2 to F6 change a value during a match and show it through Kitserver, like F1. Their set-up is called from the kick function's entry until done; their state is in the 32 bytes before the close dribbling ones (`0xBC3FD0`). F2 to F5 step by 10 %, F6 by 25 % (the texts for every step sit in caves of their own).

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

---

## 9. Known limitations

- The menu backgrounds are only 512 wide: black bars on the sides.
- In the main menu, a second copy of the player picture shows in the right bar.
- The free direction follows the stick's precise angle when running and for the most frequent dribble touch; some less frequent touches and special moves keep the game's directions.
- Only the reference build has been tested in game; other builds are patched when their code matches.
