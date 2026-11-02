# À faire

*[English](TODO.md) | Français*

Améliorations du gameplay prévues pour PES6 Patcher, sans ordre particulier.

## Joueurs IA : dribble à 360°

Les dribbleurs de l'IA se déplacent toujours selon les quelques directions fixes du jeu (surtout des multiples de 45°), alors que les joueurs humains suivent l'angle précis du stick.

Un premier essai (novembre 2026) a été mis de côté, faute de changement visible en jeu. Ce qu'il en reste :
- les directions de l'IA sont déjà arrondies au moment où elle les décide, pas par la fonction de dribble ;
- le champ `+0x2C` du joueur contient très probablement la direction visée par l'IA, avec un angle précis ;
- l'utiliser ne changeait chaque touche que de 2 à 13°.

## Vitesse de course avec le ballon

Un joueur rapide balle au pied ne parvient presque jamais à distancer un défenseur qui le poursuit : les débordements sur les ailes sont quasi impossibles. La vitesse balle au pied devrait davantage récompenser les joueurs rapides.

## Position des joueurs

Les joueurs sont globalement trop rapprochés. Le jeu paraît daté, il est plus difficile, et on ne peut pas vraiment construire ses actions. Il faudrait pouvoir les écarter un peu.

## Joueurs humains : dribbler dans n'importe quelle direction

La direction libre suit l'angle précis du stick, mais certaines touches de dribble et certains gestes gardent les directions du jeu. Un joueur peut-il vraiment aller n'importe où ?
