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

## Passes en profondeur

Les passes en profondeur vont au bon coéquipier mais loin devant lui, et sur les ailes elles sortent presque toujours en touche.

Fait : **Passes en profondeur moins loin devant le receveur** réduit l'avance que le jeu donne au point visé (60 % semble juste en jeu, F9 pour comparer).

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

Les passes en hauteur (rond sur une manette PlayStation) sont souvent trop fortes : le ballon va trop loin. Leur puissance devrait être réglable, comme celle des tirs.

Fait autrement : le jeu choisit le receveur d'après la jauge et le stick, puis donne au ballon la vitesse pour l'atteindre ; réduire la vitesse faisait seulement tomber le ballon trop court. **Passes en hauteur : l'appui règle la distance, le stick la direction** fait viser plus près sur un appui court et resserre le cône autour du stick. Les centres ne changent pas.

## Têtes : puissance et précision

Issue : [#10](https://github.com/kwentinn/PES6Patcher/issues/10)

Les têtes sont souvent très imprécises, comme l'étaient les tirs, et si on appuie trop longtemps, le ballon part bien au-dessus de la barre. Elles ne passent peut-être pas par la fonction de tir que modifient les options de tir : trouver leur chemin, puis réduire leur puissance et leur écart de visée de la même façon.

## Joueurs humains : dribbler dans n'importe quelle direction

Issue : [#8](https://github.com/kwentinn/PES6Patcher/issues/8)

La direction libre suit l'angle précis du stick, mais certaines touches de dribble et certains gestes gardent les directions du jeu. Un joueur peut-il vraiment aller n'importe où ?

Fait pour le stick : le jeu lisait un stick tenu à moins d'environ 16° d'un axe pile sur cet axe (une zone morte de 29 % par axe), si bien que le joueur continuait sur une ligne. **Zone morte du stick analogique réduite** l'abaisse (6 % par défaut). Reste ouvert : certains gestes de rotation envoient le ballon sur des multiples de 45° ou de 22,5°.

## Joueurs humains : changements de direction brusques balle au pied

Issue : [#11](https://github.com/kwentinn/PES6Patcher/issues/11)

Quand on tourne brusquement le stick en dribblant, le joueur ne change pas de direction d'un coup : il tourne par paliers, touche après touche. Tourner en une fois (dans la limite de ce que permettent ses stats) rendrait le dribble beaucoup plus vif.

Fait en sprint : **Virages plus francs en sprint, selon la précision de dribble** laisse le ballon tourner jusqu'à 90° par touche en sprint (le jeu : 22,5°) pour les meilleurs dribbleurs ; le joueur tourne avec le ballon à la touche de chaque pied, et à partir de 45° l'animation de virage du jeu se joue. Le joueur s'éloignait alors du ballon, sa course ne tournant que de 1,4° par image : **Le joueur tourne plus vite avec le ballon** relève ce pas (22° par image par défaut). Reste ouvert : les foulées de course sans sprint n'ont pas été changées.

## Joueurs humains : demi-vitesse de dribble

Issue : [#12](https://github.com/kwentinn/PES6Patcher/issues/12)

Suggestion de H_G sur Evo-Web : dans PES5, maintenir R2 en dribblant donnait une vitesse plus lente et contrôlée, entre la marche et la course ; PES6 l'a retirée. Il faudrait d'abord savoir si PES6 a encore le code de cette vitesse, ou s'il a été complètement supprimé.

## Ballon : plus d'effet

Issue : [#13](https://github.com/kwentinn/PES6Patcher/issues/13)

Suggestion de SrBurrito sur Evo-Web : plus de courbe sur les tirs et passes enveloppés. La rotation du ballon est dans ses données ; l'effet Magnus déjà trouvé n'agit que sur la hauteur, il faut donc d'abord trouver où la rotation fait dévier le ballon sur le côté.

## Penalties : contrôle de la puissance

Issue : [#14](https://github.com/kwentinn/PES6Patcher/issues/14)

Suggestion de kovy sur Evo-Web : un contrôle de la puissance des penalties. Les penalties sont des coups de pied arrêtés et gardent la puissance du jeu ; il faut d'abord trouver comment elle est décidée.

## Passes manuelles : plus d'angles

Issue : [#15](https://github.com/kwentinn/PES6Patcher/issues/15)

Suggestion de kovy sur Evo-Web : plus d'angles pour la passe manuelle (stick droit). Les passes passent par du code protégé ; il faut d'abord vérifier si la direction de la passe manuelle est arrondie à quelques secteurs, et où.

## Écart d'accélération entre joueurs

Issue : [#16](https://github.com/kwentinn/PES6Patcher/issues/16)

Les joueurs à forte accélération, comme Messi, se distinguent à peine dans le jeu : ils devraient faire la différence sur les premiers pas, comme le fait **Écart de vitesse plus marqué** pour la vitesse de pointe. Déjà connu : à chaque image, `0x48FEF0` rapproche la vitesse de course (`+0x1C`) de la vitesse visée, avec un temps d'accélération tiré de la stat d'accélération (`0x3BCF5B0`). Il faudrait d'abord mesurer de combien ce temps change entre une stat faible et une stat élevée, puis élargir l'écart de la même façon.

## Écran large : écran de démarrage décalé à gauche (bug)

Issue : [#17](https://github.com/kwentinn/PES6Patcher/issues/17)

Signalé par denton_ajax et rds084 sur Evo-Web : en écran large, l'écran de démarrage (« Press Start ») est décalé vers la gauche, avec ou sans Menu Server. Il suppose sans doute encore la couche 2D d'origine, large de 512 ; il faut d'abord trouver quel appel de dessin le place.

## Arbitres : plus de fautes et de penalties

Issue : [#18](https://github.com/kwentinn/PES6Patcher/issues/18)

Suggestion de bukowski sur Evo-Web : les arbitres de PES6 sifflent trop peu de fautes, et les penalties sont très rares. Il faut d'abord trouver où un contact est jugé fautif.

## Flèches de forme : effet réglable

Issue : [#19](https://github.com/kwentinn/PES6Patcher/issues/19)

Suggestion de liqiao806 et SrBurrito sur Evo-Web : régler de combien chaque flèche de forme augmente ou baisse les stats. Les flèches changent les stats pendant le match, le code qui les applique devrait donc pouvoir être retrouvé à partir des stats.

## Joueurs humains : pousser le ballon encore plus loin (R1 trois fois)

Appuyer deux fois sur R1 en sprint pousse le ballon plus loin devant. Un troisième appui pourrait le pousser encore plus loin, pour partir dans l'espace. Il faut d'abord trouver où le double appui est détecté et à quelle distance il envoie le ballon.

## Coups de pied arrêtés : stick trop sensible

Issue : [#20](https://github.com/kwentinn/PES6Patcher/issues/20)

Fait : les visées des corners et des coups francs tournaient à pleine vitesse dès que le stick penchait à droite ou à gauche, quelle que soit la poussée. Avec **Zone morte du stick analogique réduite**, elles ignorent maintenant une poussée sous 3/8 et tournent selon le carré de la poussée, jusqu'à la vitesse du jeu.

## IA : le gardien monte sur les derniers corners

Issue : [#21](https://github.com/kwentinn/PES6Patcher/issues/21)

Suggestion de daffi2 sur Evo-Web : quand une équipe est menée en fin de match, son gardien pourrait monter dans la surface adverse sur les corners. Il faut d'abord trouver comment l'IA choisit les joueurs qui montent sur un coup de pied arrêté.

## Joueurs humains : changer de joueur au stick droit

Issue : [#22](https://github.com/kwentinn/PES6Patcher/issues/22)

Suggestion de jean noel sur Evo-Web : choisir le joueur à contrôler en pointant le stick droit vers lui, comme dans les PES suivants. Il faut d'abord trouver où le jeu choisit le joueur suivant au changement.

## IA : défendre en bloc

Issue : [#23](https://github.com/kwentinn/PES6Patcher/issues/23)

Suggestion de Tinker sur Evo-Web : en défense, les défenseurs se regroupent, les milieux défensifs rejoignent la ligne arrière et les défenseurs reculent jusqu'à leur surface au lieu de tenir une ligne. **Joueurs plus écartés en largeur** aide déjà en largeur ; tenir une ligne défensive demande de changer le placement des joueurs sans le ballon.

## Vitesse de jeu : la vitesse x2 de la boutique

Issue : [#24](https://github.com/kwentinn/PES6Patcher/issues/24)

Suggestion de bukowski sur Evo-Web : la boutique PES déverrouille une vitesse de jeu x2. Comprendre comment elle marche pourrait montrer où le jeu règle sa vitesse, pour un vrai réglage de vitesse (Speeder ralentit tout le moteur).
