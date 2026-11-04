# To do

*English | [Français](TODO.fr.md)*

Gameplay improvements planned for PES6 Patcher, in no particular order. Each open item has a [GitHub issue](https://github.com/kwentinn/PES6Patcher/issues), where it can be discussed.

## AI players: 360° dribbling

Issue: [#2](https://github.com/kwentinn/PES6Patcher/issues/2)

AI dribblers still move in the game's few fixed directions (mostly multiples of 45°), while human players follow the stick's precise angle.

A first try (November 2026) was shelved, because no change was visible in play. Findings:
- the AI's directions are already rounded when it decides them, not by the dribble function;
- the player field `+0x2C` very likely holds the direction the AI aims at, with a fine angle;
- using it only changed each touch by 2 to 13°.

## Running speed with the ball

A fast player with the ball almost never gets past a chasing defender, so beating a defender on the wing is nearly impossible. Speed with the ball should reward pace more.

Done: **Bigger speed gap between fast and slow players** makes the top speed stat weigh up to 4 times more on the maximum running speed (300 % feels right in game). The acceleration (stat at `0x3BCF5B0`) and the dribble touches are left as they are.

## Player positions

Players stand too close to each other overall. The game feels dated, harder, and it is difficult to really build moves.

Done for the width: **Players spread wider** moves the players placing themselves away from their team's centre across the pitch (20 to 30 % feel right in game). Spreading the length too made the defenders drop too close to their keeper, so it was left out.

## Through passes

Through passes reach the right team-mate but far ahead of him, and on the wings they almost always go out of play.

Done: **Through passes less far ahead of the receiver** shortens the lead the game gives the pass point (60 % feels right in game, F9 to compare).

## Pass power bar

Issue: [#5](https://github.com/kwentinn/PES6Patcher/issues/5)

Suggested by dansef on Evo-Web: change how the power bar fills for normal and through passes. It may not be doable: when looking at the shot gauge, the part that measures how long the button is held turned out to sit in the protected code of the exe.

## AI players: movement without the ball

Issue: [#3](https://github.com/kwentinn/PES6Patcher/issues/3)

Noted by Energia and maruni74 on Evo-Web: without the ball, the AI's players still run in the game's 8 directions, while human players follow the stick's angle. Not looked at yet.

## AI players: dribble more

Issue: [#4](https://github.com/kwentinn/PES6Patcher/issues/4)

Suggested by amsterdance on Evo-Web: the AI's skilful players (Ronaldinho, Zidane…) mostly pass at once or make small moves. They could try to dribble more. This means finding where the AI decides between passing, shooting and dribbling.

## Menus: widen only the background

Issue: [#6](https://github.com/kwentinn/PES6Patcher/issues/6)

Suggested by daffi2 on Evo-Web: keep the menu items at their original size and only widen the background image, as an option, to remove the black bars. This means finding how the backgrounds are drawn. Also seen by daffi2: in the kit selection, the Kitserver mini kit is widescreen while the game's one is not.

## Kitserver module instead of patching the exe

Issue: [#7](https://github.com/kwentinn/PES6Patcher/issues/7)

Suggested by daffi2 on Evo-Web: apply the patch from a Kitserver DLL, in memory when the game starts, so that `PES6.exe` stays untouched. Other modules, like Nemesis, require an untouched exe. The added code would be the same; the DLL would write it into the running game and read its settings from a file.

## Lofted passes: adjustable power

Issue: [#9](https://github.com/kwentinn/PES6Patcher/issues/9)

Lofted passes (circle on a PlayStation pad) are often too strong: the ball goes too far. Their power should be adjustable, like the shots'.

Done another way: the game chooses the receiver from the gauge and the stick, then gives the ball the speed to reach him, so scaling the speed only made the ball fall short. **Lofted passes: the press sets the distance, the stick the direction** makes a short press aim nearer and narrows the cone around the stick. Crosses are unchanged.

## Headers: power and accuracy

Issue: [#10](https://github.com/kwentinn/PES6Patcher/issues/10)

Headers are often very inaccurate, like the shots were, and when the button is held too long the ball goes well over the bar. They may not go through the shot function that the shot options hook: find their path, then bring their power and aim error down the same way.

## Human players: dribbling in any direction

Issue: [#8](https://github.com/kwentinn/PES6Patcher/issues/8)

The free direction follows the stick's precise angle, but some dribble touches and moves still use the game's own directions. Can a player really go anywhere?

## Human players: sharp changes of direction with the ball

Issue: [#11](https://github.com/kwentinn/PES6Patcher/issues/11)

When the stick is turned suddenly while dribbling, the player does not change direction at once: he turns in several steps, touch after touch. Turning in one go (within what the player's stats allow) would make dribbling much livelier.

Done when sprinting: **Sharper turns when sprinting, by dribble accuracy** lets the ball turn up to 90° per sprint touch (the game: 22.5°) for the best dribblers, and shortens such a touch. Still open: the player's body keeps the game's turning step for a moment (forcing it made the animation jump), and the running strides without sprint were not changed.

## Human players: semi dribbling speed

Issue: [#12](https://github.com/kwentinn/PES6Patcher/issues/12)

Suggested by H_G on Evo-Web: in PES5, holding R2 while dribbling gave a slower, controlled speed between walking and running; PES6 removed it. First find out whether PES6 still has the code for that speed, or whether it was removed completely.

## Ball: more swerve

Issue: [#13](https://github.com/kwentinn/PES6Patcher/issues/13)

Suggested by SrBurrito on Evo-Web: more curve on curled shots and passes. The ball's spin is in its data; the spin lift already found only acts on the height, so first find where the spin bends the ball sideways.

## Penalties: control of the power

Issue: [#14](https://github.com/kwentinn/PES6Patcher/issues/14)

Suggested by kovy on Evo-Web: some control of the penalty kick's power. Penalties are set pieces and keep the game's power; first find how it is decided.

## Manual passes: more angles

Issue: [#15](https://github.com/kwentinn/PES6Patcher/issues/15)

Suggested by kovy on Evo-Web: more angles for manual passing (right stick). Passes go through protected code; first check whether the manual pass direction is rounded to a few sectors, and where.

## Acceleration gap between players

Issue: [#16](https://github.com/kwentinn/PES6Patcher/issues/16)

Players with a great acceleration, like Messi, hardly stand out in the game: they should make the difference over the first steps, the way **Bigger speed gap** does for the top speed. Already known: every frame, `0x48FEF0` moves the running speed (`+0x1C`) towards the target speed with an acceleration time taken from the acceleration stat (`0x3BCF5B0`). First measure how much that time changes between a low and a high stat, then widen the gap the same way.

## Widescreen: start screen shifted to the left (bug)

Issue: [#17](https://github.com/kwentinn/PES6Patcher/issues/17)

Reported by denton_ajax and rds084 on Evo-Web: with the widescreen display, the start screen ("Press Start") is shifted to the left, with or without Menu Server. It probably still assumes the original 512-wide 2D layer; first find which drawing call places it.

## Referees: more fouls and penalties

Issue: [#18](https://github.com/kwentinn/PES6Patcher/issues/18)

Suggested by bukowski on Evo-Web: PES6's referees whistle too few fouls, and penalties are very rare. First find where a contact is judged a foul.

## Condition arrows: adjustable effect

Issue: [#19](https://github.com/kwentinn/PES6Patcher/issues/19)

Suggested by liqiao806 and SrBurrito on Evo-Web: set how much each condition arrow raises or lowers the stats. The arrows change the stats during a match, so the code applying them should be findable from the stats.
