# To do

*English | [Français](TODO.fr.md)*

Gameplay improvements planned for PES6 Patcher, in no particular order.

## AI players: 360° dribbling

AI dribblers still move in the game's few fixed directions (mostly multiples of 45°), while human players follow the stick's precise angle.

A first try (November 2026) was shelved, because no change was visible in play. Findings:
- the AI's directions are already rounded when it decides them, not by the dribble function;
- the player field `+0x2C` very likely holds the direction the AI aims at, with a fine angle;
- using it only changed each touch by 2 to 13°.

## Running speed with the ball

A fast player with the ball almost never gets past a chasing defender, so beating a defender on the wing is nearly impossible. Speed with the ball should reward pace more.

## Player positions

Players stand too close to each other overall. The game feels dated, harder, and it is difficult to really build moves.

Done for the width: **Players spread wider** moves the players placing themselves away from their team's centre across the pitch (20 to 30 % feel right in game). Spreading the length too made the defenders drop too close to their keeper, so it was left out.

## Human players: dribbling in any direction

The free direction follows the stick's precise angle, but some dribble touches and moves still use the game's own directions. Can a player really go anywhere?
