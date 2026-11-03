# À faire

*[English](TODO.md) | Français*

Améliorations du gameplay prévues pour PES6 Patcher, sans ordre particulier. Chaque piste ouverte a une [issue GitHub](https://github.com/kwentinn/PES6Patcher/issues), où elle peut être discutée.

## Joueurs IA : dribble à 360°

Issue : [#2](https://github.com/kwentinn/PES6Patcher/issues/2)

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

Issue : [#5](https://github.com/kwentinn/PES6Patcher/issues/5)

Suggestion de dansef sur Evo-Web : changer le remplissage de la barre de puissance pour les passes normales et les passes en profondeur. Ce n'est peut-être pas faisable : en étudiant la jauge de tir, la partie qui mesure la durée d'appui s'est révélée être dans le code protégé de l'exe.

## Joueurs IA : déplacements sans ballon

Issue : [#3](https://github.com/kwentinn/PES6Patcher/issues/3)

Remarqué par Energia et maruni74 sur Evo-Web : sans ballon, les joueurs de l'IA courent toujours selon les 8 directions du jeu, alors que les joueurs humains suivent l'angle du stick. Pas encore étudié.

## Joueurs IA : dribbler davantage

Issue : [#4](https://github.com/kwentinn/PES6Patcher/issues/4)

Suggestion d'amsterdance sur Evo-Web : les joueurs techniques de l'IA (Ronaldinho, Zidane…) font le plus souvent une passe tout de suite ou de petits mouvements. Ils pourraient tenter davantage de dribbles. Il faudrait trouver où l'IA choisit entre passer, tirer et dribbler.

## Menus : élargir seulement le fond

Issue : [#6](https://github.com/kwentinn/PES6Patcher/issues/6)

Suggestion de daffi2 sur Evo-Web : garder les éléments des menus à leur taille d'origine et n'élargir que l'image de fond, en option, pour supprimer les bandes noires. Il faudrait trouver comment les fonds sont dessinés. Remarqué aussi par daffi2 : dans la sélection des tenues, le mini-maillot de Kitserver est en écran large, celui du jeu non.

## Module Kitserver au lieu de patcher l'exe

Issue : [#7](https://github.com/kwentinn/PES6Patcher/issues/7)

Suggestion de daffi2 sur Evo-Web : appliquer le patch depuis une DLL Kitserver, en mémoire au démarrage du jeu, pour que `PES6.exe` reste intact. D'autres modules, comme Nemesis, exigent un exe d'origine. Le code ajouté serait le même ; la DLL l'écrirait dans le jeu en cours d'exécution et lirait ses réglages dans un fichier.

## Passes en hauteur : puissance réglable

Issue : [#9](https://github.com/kwentinn/PES6Patcher/issues/9)

Les passes en hauteur (rond sur une manette PlayStation) sont souvent trop fortes : le ballon va trop loin. Leur puissance devrait être réglable, comme celle des tirs. Toutes les passes passent par la fonction de frappe : trouver l'appel de la passe en hauteur et réduire sa puissance pour les équipes humaines pourrait suffire.

## Têtes : puissance et précision

Issue : [#10](https://github.com/kwentinn/PES6Patcher/issues/10)

Les têtes sont souvent très imprécises, comme l'étaient les tirs, et si on appuie trop longtemps, le ballon part bien au-dessus de la barre. Elles ne passent peut-être pas par la fonction de tir que modifient les options de tir : trouver leur chemin, puis réduire leur puissance et leur écart de visée de la même façon.

## Joueurs humains : dribbler dans n'importe quelle direction

Issue : [#8](https://github.com/kwentinn/PES6Patcher/issues/8)

La direction libre suit l'angle précis du stick, mais certaines touches de dribble et certains gestes gardent les directions du jeu. Un joueur peut-il vraiment aller n'importe où ?
