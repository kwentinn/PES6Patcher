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

Fait : **Écart de vitesse plus marqué entre joueurs rapides et lents** donne jusqu'à 4 fois plus de poids à la stat de vitesse de pointe sur la vitesse maximale (300 % semble juste en jeu). L'accélération (stat en `0x3BCF5B0`) et les touches de dribble ne changent pas.

## Position des joueurs

Les joueurs sont globalement trop rapprochés. Le jeu paraît daté, il est plus difficile, et on ne peut pas vraiment construire ses actions.

Fait pour la largeur : **Joueurs plus écartés en largeur** éloigne les joueurs qui se replacent du centre de leur équipe en largeur (20 à 30 % semblent justes en jeu). Écarter aussi en longueur faisait reculer les défenseurs trop près de leur gardien : ce n'est donc pas fait.

## Barre de puissance des passes

Suggestion de dansef sur Evo-Web : changer le remplissage de la barre de puissance pour les passes normales et les passes en profondeur. Ce n'est peut-être pas faisable : en étudiant la jauge de tir, la partie qui mesure la durée d'appui s'est révélée être dans le code protégé de l'exe.

## Joueurs humains : dribbler dans n'importe quelle direction

La direction libre suit l'angle précis du stick, mais certaines touches de dribble et certains gestes gardent les directions du jeu. Un joueur peut-il vraiment aller n'importe où ?
