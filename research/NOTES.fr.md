# PES6 — notes de recherche

*[English](NOTES.md) | Français*

Comment les correctifs de PES6 Patcher ont été trouvés et ce qu'ils modifient. La « v6 » est la première version de `PES6.exe` validée en jeu (écran large et directions libres), en 16:9 et en 32:9, que l'application reproduit toujours octet pour octet. Les options de gameplay ajoutées depuis sont décrites dans la section 3.

Les adresses sont celles de la version de référence (`PES6.exe` « No-DVD », SHA-256 `3FE13850…`). L'application retrouve chaque emplacement par signature : les autres versions qui contiennent le même code sont patchées aussi (section 5).

---

## 1. Réglages modifiés (hors exe), pendant la recherche en 16:9

| Fichier | Réglage | Valeur |
|---|---|---|
| `kitserver\kload.cfg` | `dx.fullscreen.width/height`, `internal.resolution.width/height` | 2560 × 1440 |
| `kitserver\speeder.cfg` | `count.factor` | 1.0 (vitesse d'origine) |
| `kitserver\lodmixer.dat` | format d'image manuel (octets 41–44) | 1.77778 (16:9) |
| `dgVoodoo.conf` | `ScalingMode` | `stretched_ar` (image centrée, bandes noires) |

---

## 2. Modifications de `PES6.exe` (v6)

L'original est conservé dans **`PES6.exe.orig`**, à côté du jeu.

Le repère 2D des menus de PES6 est hérité de la PS2 : 512 × 448, centré. Le jeu l'étirait sur toute la largeur de l'écran. Le patch élargit ce repère à **796 × 448** (16:9). La 3D et le gameplay continuent d'utiliser les valeurs d'origine.

| Adresse | Modification | Rôle |
|---|---|---|
| `0xBC3F00` / `0xBC3F04` | nouvelles constantes 398 et 1/796 (bourrage de `.rdata`) | demi-largeur et échelle du nouveau repère 2D |
| 12 instructions (`0x78ED90` … `0x941B98`) et `0x87AEE4` | lisent 398 et 1/796 au lieu de 256 et 1/512 | conversion repère 2D → pixels |
| `0x8744F0` | `return 796` au lieu de `return 512` | largeur 2D (mise en page, découpage, curseur) |
| `0x661123`, `0xB3F939` | `call 0x8744F0` → `mov eax, 0x200` | la projection 3D garde 512 (sinon les joueurs « glissent ») |
| `0xBEE2D4`, `0xBEE2E4` | rectangles de vue 2D initiaux élargis | cohérence du repère 2D |
| `0x40D740` → code ajouté en `0x403500` (60 octets) | rééchelonne x par 512/796 | découpage des listes (`SetViewport`) |
| `0x40CE95` → code ajouté en `0x403540` (19 octets), constante `0xBC3F08` | échelle x × 1,5547 | position du curseur bleu au-dessus du joueur |
| `0x4B27E0` → code ajouté en `0x404338` (156 octets) | ajoute l'écart réel du stick au secteur de 45° | directions libres au stick analogique |
| `0x5AF3CF` | seuil de virage `0x20` → `0x0C` (45° → environ 17°) | virage déclenché par les petites corrections |

Points importants :

- Les instructions qui écrivent dans les données du match (`0x656xxx`, `0x657A2E`, `0x6D1AE9`, `0x8DBA70`, `0x8DD430`) ne sont **pas** redirigées : elles faisaient glisser les joueurs.
- Les « tables de projection » `0xD4FB98…` ne sont **pas** modifiées pour la même raison.
- Les directions libres ne concernent que le joueur humain au stick analogique. La croix directionnelle, le clavier et l'IA ne changent pas.

---

## 3. Options de gameplay (après la v6)

Chaque option est un détournement : quelques octets du jeu remplacés par un saut vers du code ajouté dans le bourrage `CC`, qui rejoue les instructions remplacées. Elles ont été trouvées avec des points d'arrêt matériels et des relevés mémoire dans le jeu en cours d'exécution (section 5), puis vérifiées en jeu.

### Frappe et touches de dribble

- Chaque touche, passe et tir passe par la **fonction de frappe** `0x478020` (joueur, direction, vitesse, hauteur, drapeau). Les touches de dribble passent par un **relais** `0x4781F0` (joueur, direction, vitesse, hauteur).
- Les touches de dribble sont reconnues à leur adresse de retour : en course `0x5AF6D3`, `0x5AF0CF`, `0x5ADEC6`, `0x5B0D65`, `0x5B07A5` ; en sprint `0x5AFB40`, `0x5AFE6D`. Les signatures ont été allongées, car le même code existe dans d'autres états.
- **Dribble à 360°** (directions libres) : la touche en `0x5ADEC6` reçoit l'angle du stick arrondi à 22,5° par du code virtualisé. À l'entrée du relais, pour la manette dont le stick donne cette direction arrondie (ou son opposée : côté d'attaque), l'arrondi est ajouté de nouveau. L'IA ne correspond à aucune manette et n'est pas touchée.
- **Dribble resserré** : à l'entrée du relais, la vitesse des 7 touches de dribble est réduite (× 14/16 en course, × 13/16 en sprint), en proportion de la précision de dribble du joueur (stat `0x3BCF5B2`, 14 octets avant la technique `0x3BCF5C0` que le jeu lit pour l'imprécision de direction des touches ; jusqu'à la 1.26 le patcher lisait cette technique par erreur, comme l'a remarqué kanzi123 dans #32) : rien jusqu'à 65, la réduction complète à partir de 95.
- **F1** active et désactive le dribble resserré. Le message passe par Kitserver : `kload.dll` exporte `HookFunction` (3 = `Present` de Direct3D, rappel stdcall à 5 arguments) et `KDrawText` (x, y, couleur, taille, texte, absolu ; base 640 × 480). L'état est dans les 16 derniers octets de `.rdata` (`0xBC3FF0`), rendue inscriptible.

### Intro

- Vidéo d'ouverture : la fonction de la vidéo `0x88E350` sait déjà s'arrêter quand on le lui demande ; son `je` en `0x88E361` est neutralisé.
- Logos : un logo marqué comme passable se termine dès qu'on appuie sur une touche ; les deux tests (`0x9AFAD5` drapeau, `0x9AFAE4` touche) sont neutralisés. Le chargement fait pendant les logos (fichier d'options, etc.) a toujours lieu.
- Fausses pistes : sauter l'état de démarrage 1 supprimait l'écran de langue, sauter l'étape de chargement 1 sautait le fichier d'options (plantage), et forcer la minuterie des logos bloquait le jeu.

### Coups francs

- Un tir de coup franc direct est l'appel de la **fonction de tir** par les coups de pied arrêtés, en `0x58CC11` (→ `0x5B9130`). Les tirs du jeu courant appellent la même fonction depuis 18 autres endroits.
- Le vol du ballon `0x475810` (utilisé aussi par les prédictions de trajectoire du gardien et de l'IA) ajoute le poids du ballon avec `fadd [0xB8AE70]` en `0x475EEF` (188,0). Le mod de physique de PityCharly réécrit cette constante pendant le jeu : le poids en plus est donc ajouté par-dessus.
- L'appel des coups de pied arrêtés lit les sticks gauches (table `0x1016680` : angle, amplitude, bits de direction, 4 octets par manette) : stick en haut → le ballon de ce tir reçoit le poids en plus jusqu'à la frappe suivante (l'entrée de la fonction de frappe l'efface). Le jeu lit le stick au même moment pour l'effet du tir, et plus du tout pendant le vol (champs du ballon `+0x6A`, `+0x6C`, `+0x74`).

### Tirs

- **Tirs plus doux** : à l'entrée de la fonction de tir, l'argument de puissance au-delà de 0xC0 (la plus petite base des types de tir) est réduit. Les coups de pied arrêtés gardent la puissance du jeu.
- **Tirs plus précis** : l'appel de la fonction de frappe par le tir (`0x5BBC9A`) rapproche la direction et la hauteur finales de la direction demandée, gardée à l'entrée. Un tir du mauvais pied garde le carré de la part réglée : le jeu compare le pied de l'animation (`[joueur+4]+0x2D`) au pied fort (stat `0x3BCF5A8`, bit 0) en `0x544270`, et donne à ce tir moins de puissance et un écart plus grand, toujours du même côté.
- **Sous pression** : `0x545C20` (appelée en `0x5B9907` et `0x5B9C90`) reconnaît un tir sous pression : un adversaire à environ 1 m, ou entre 2 et 4 m dans l'axe du tir, ou à environ 3 m dans l'axe sauf si un tirage sur la technique de tir du tireur (stat `+0x12` ; prise pour la précision de tir jusqu'à la 1.26) réussit ; un tirage entre une stat du défenseur et du tireur peut l'annuler. Sous pression, l'erreur de direction est divisée par 7 à 9 au lieu de 9 à 11 (`0x5B9A51`), et un second tirage sur la précision de tir change la puissance ou la hauteur (`0x5B9CB1`). Mesuré : un tir d'un grand attaquant sous pression, jauge forte, est parti à une hauteur de 63 (environ 30 d'habitude). **Tirs plus précis** fait maintenant suivre sa part à la précision de tir : gardée jusqu'à 75, 4 % de moins par point au-dessus, au moins 20 %.
- **Mauvais pied** : la précision du mauvais pied (stat `+0x1C`, bits 11 à 13, 0 à 7) donne un facteur de 130, 120, 110, 108, 106, 104, 102 ou 101 % (`0x4C7EA0`) : un tir humain du mauvais pied perd cet excédent de puissance (`0x5B9777`) ; la jauge de tir de l'IA (`0x544EB0`) en gagne autant quand le mauvais pied est noté 3 ou moins (affiché 1 à 4) et qu'un tirage sur la précision de tir échoue.
- **Équipes humaines** : à chaque frappe, pour chaque manette dont le stick est poussé, le joueur actif de chaque équipe gagne +3 si sa direction demandée est celle du stick (ou l'opposée), −2 sinon (+1 jusqu'à la 1.22 : il fallait une quarantaine de frappes, et dans un match mesuré le premier tir de l'humain, sous pression dans la première minute, est parti sans correction à deux fois la hauteur habituelle). Une équipe à 32 ou plus (sur 64) est humaine. Les tirs de l'IA ne sont pas touchés. Le score est rangé selon l'équipe du joueur actif (`+0x12`) : les emplacements des joueurs actifs suivent les côtés, et à la mi-temps les équipes échangent leurs numéros (1–11 et 12–22). Jusqu'à la 1.22 le score était rangé par emplacement : en seconde mi-temps, les tirs de l'humain n'étaient plus corrigés et ceux de l'IA l'étaient (mesuré par points d'arrêt sur l'appel de frappe du tir, hauteur avant et après la correction). Sans correction, un tir à jauge presque pleine peut partir avec une hauteur de 57 à 65 au lieu d'environ 30.
- **Types de tir** : un tir est l'état 4 du joueur. Son code `0x5BBF90` fait un aiguillage sur `+0x18` (`0x00`…`0x27`, table en `0x5BD858`, cas en `0x5BD800`) ; 17 valeurs sont des types de tir (`0x10`–`0x14`, `0x16`, `0x18`, `0x1B`–`0x1E`, `0x21`–`0x25`, `0x27`), chacune appelant la fonction de tir `0x5B9130` depuis son propre endroit (`0x5BC321` … `0x5BD7BA`) avec sa propre base de puissance (`0x300` à `0xA00`) et ses paramètres. Le type est choisi par le code de décision du tir, de `0x544954` à `0x54980A` (19 endroits qui fixent l'état 4 et `+0x18`), plus `0x579AEB` (type `0x27`) et les coups de pied arrêtés (`0x58C026`, `0x58C323`). Quel type est lequel (tir en course, reprise de volée, retourné…) n'a pas été établi : lu au début d'un tir, `+0x18` contenait une phase (0 ou 1), pas le type.
- La fonction de tir tire beaucoup de nombres aléatoires (`0x483EF0`, `0x483F40`, une quarantaine d'appels entre `0x5B93CB` et `0x5B9E59`) avant l'appel de frappe `0x5BBC9A` ; c'est pourquoi la précision agit sur la direction et la hauteur finales de l'appel de frappe plutôt que sur chaque tirage.
- **Lobs** : un lob est un tir dont le tireur tient le bouton `0x400` (L1) dans `+0xB0` au départ du ballon (tous les lobs relevés ; aucun des autres tirs). Sonde de frappe (`pes6probe.ps1`, branche `kick-probe`) : les lobs classiques utilisent les animations `0xC9`, `0xCA`, `0x244`, `0x245` et partent à une hauteur de 45 à 68, contre 13 à 35 pour les tirs ; les lobs mal placés utilisent d'autres animations (`0xF`, `0xEE`) et partent bas. La précision réduisait toute hauteur au-dessus de 20 comme une erreur, ce qui aplatissait les lobs (la moitié de leur hauteur à 50 %, presque tout à 1 %). Un lob garde maintenant 95 % de sa hauteur et reçoit la puissance des lobs (70 % par défaut) sur sa vitesse. Essayé puis abandonné : reconnaître les lobs à leur hauteur (rate les lobs bas) ; réduire l'argument de puissance à l'entrée de la fonction de tir pour les lobs (irrégulier : la force du lob se décide plus loin).
- Mesuré en jeu avec le réglage à 1 % : les tirs sont « beaucoup plus cadrés » ; ce qui rate encore vient de la visée elle-même (la direction demandée), pas de l'écart aléatoire.
- Fausse piste : la jauge de tir. La puissance est décidée par le type de tir et des formules (par exemple `0x108 + [joueur+0xA4] × 24 / (8142 − 16 × [joueur+0x5C])`), pas par un compteur visible. La saisie humaine est recopiée dans le joueur (`+0xB0` ; le bit `0x800` était pris pour l'appui sur tir, mais mesuré à la frappe il accompagne les gâchettes droites — voir la super frappe ci-dessous) par du code protégé (vers `0x448A73D`) ; l'IA appuie sur le même bit. `[joueur+4]+0x34` est l'image de l'animation, pas un compteur d'appui.
- **Super frappe** (L1 + R1, branche `feat/super-shot`) : boutons mesurés à la frappe par du code ajouté écrivant un anneau des derniers tirs (`+0xB0` du tireur, état du joueur 4) : rien de tenu donne `0x20` (la direction seule), L1 `0x400`, R1 `0x01488800`, R2 `0x01000800`, L2 `0x02000200`, et L1 + R1 `0x01000C00` — maintenir L1 fait disparaître les bits propres à R1 (`0x488000`), il ne reste que la part commune `0x01000800` (R1 + R2 annule le tir). L'argument de vitesse du jeu mesuré sur de grosses frappes va de 18463 à 23011 ; 36300 a d'abord été validé en match, puis jugé un peu court et monté à 38000 ; 40000 paraissait alors trop rapide, mais une fois le plongeon des stars en place il est devenu le défaut de Pro (15 novembre 2026 : Normal 42000, Hardcore 38000). Forcer la vitesse et la hauteur laissait un lob : il fallait supprimer le lob lui-même. Effacer L1 à la copie protégée de la saisie (`0x448A73D`) ne fait rien (8 passages en 10 secondes : ce n'est pas l'écriture par image). Trouvé avec un point d'arrêt matériel en lecture sur le `+0xB0` du tireur : les boutons sont reconstruits chaque image par `mov ecx,[esi+0xB0] ; mov eax,[esi+0xB4] ; and ecx,400h ; or ecx,eax ; mov [esi+0xB0],ecx`, où seul L1 survit — ce `and` est le dernier endroit où la super frappe est reconnaissable — et huit `mov eax,[esi+0xB0] ; test ah,4` décident du lob (sept contigus, un avec `push edi ; xor edi,edi` intercalé ; celui des coups de pied arrêtés, à l'attente de corner, est laissé tranquille). Masquer L1 pour ces huit tant que le drapeau est levé donne l'animation et le contact de la frappe normale. Hauteur 34 validée (tirs du jeu : 13 à 35) : plus bas, le tir est si tendu qu'il rentre à tous les coups.
- Super frappe, ce que reçoit la frappe (enregistré en jeu pour les derniers départs de balle avec L1 tenu) : l'état du joueur vaut **5** quand le tir est pris en courant, 4 seulement à l'arrêt — et comme R1 est le bouton de sprint, presque toutes les super frappes sont en 5 (un test sur le seul état 4 ne s'est déclenché sur aucune). Le **5e argument** de la frappe vaut 0 pour un tir et 1 pour une passe (une passe prise dans le même état a mesuré vitesse 7424 et hauteur 0, contre 26432 / 21 et 20870 / 8 pour deux tirs) : c'est ce qui les distingue, une passe jouée en maintenant L1 et R1 partant auparavant à la vitesse du tir.
- Super frappe, distinguer un tir d'une passe : ni l'état du joueur ni le 5e argument de la frappe ne suffisent (les passes au sol arrivent en état 27 avec une hauteur négative, les passes en profondeur en état 4 ou 5 avec l'argument à 0). C'est l'**adresse de retour** à l'entrée de la frappe qui tranche : un tir revient toujours à l'appel propre à la fonction de tir (`0x5BBC9F` ici, `ShotSites.KickCall` + 5), alors que les passes mesurées revenaient à `0x71222D7E` — le mod physique de PityCharly, qui appelle la même fonction pour ses passes — ou à `0x478230`.
- Super frappe d'une star : le ballon garde du poids en plus pendant son vol, il monte au-dessus des défenseurs puis replonge sous la barre. Réglé en match sur la gravité ajoutée à chaque image (`0xB8AE70`) : 140 flottait et passait au-dessus, 198 (la valeur du mod physique ; celle du jeu est 188) était le point de départ, puis 250, 270 et enfin **300** ont donné le plongeon voulu, la hauteur finissant à **32** une fois le plongeon en place. 300 à partir de 188 fait +60 %, d'où le défaut de +55 % sur l'unité de l'option coups francs (1 % du poids du jeu). Le tireur est une star par sa précision de tir (`+0x12`, 7 bits), 90 et plus par défaut.

### Joueurs plus écartés

- L'endroit où un joueur se place passe par la **fonction de cible** `0x4C9DC0` (joueur, x, z), appelée par 45 morceaux de l'IA de placement ; son corps continue dans du code protégé. Seuls les joueurs qui se replacent (état `+0x16` = 7) l'utilisent.
- Le code ajouté éloigne la cible du centre des cibles de l'équipe (`+0x6C` / `+0x70` de ses joueurs de champ), en largeur seulement (z). Écarter aussi en longueur (x) faisait reculer les défenseurs près de leur gardien.

### Écart de vitesse

- La vitesse de course est `[joueur+0x1C]`. À chaque image, `0x48FEF0` la rapproche d'une vitesse visée donnée par l'appelant (sprint balle au pied : `0x4C20F9`), avec un temps d'accélération tiré de la stat `0x3BCF5AF`.
- La **vitesse maximale** `[joueur+0x58]` est calculée, et recalculée pendant le match, en `0x447BF52` (section `.rld` de la protection, code en clair) : 7542 + 12 × (vitesse de pointe − 40) × 100 / 60 − 16 × `[joueur+0x5C]`. La stat de vitesse de pointe est l'octet `0x3BCF5AE` (corrélation de 1,00 avec `+0x58` sur 22 joueurs). Un point de stat ne vaut que 20 unités : 68 → 8094, 97 → 8682.
- Le code ajouté multiplie la part de la stat autour d'un joueur à 75. Comme la valeur est recalculée pendant le match, F6 s'applique tout de suite.

### Écart d'accélération

- `0x48FEF0` est un saut vers la protection (`0x44B6A20`) ; ses quelque 70 appelants passent (joueur, vitesse visée, temps d'accélération). Tous calculent ce temps de la même façon : une base (`0x1C`, `0x1E`, `0x28` ou `0x30`) + un champ du joueur (`+0x5C` ou `+0x5E`) − `0x483E10`(stat d'accélération `0x3BCF5AF`, 50) / 10. `0x483E10`(a, b) = (a − b) × 100 / (100 − b) au-dessus de b, 0 sinon : la stat ne retire donc que 0 à 9 d'un temps d'environ 28 à 48.
- Le /10 est `mov eax,66666667h ; imul ecx ; sar edx,2`. En mémoire, `sar edx,1` aux 55 endroits qui suivent une lecture de la stat (x 2) a semblé juste ; `sar edx,0` (x 4) rendait négatifs les temps des meilleurs joueurs : un joueur traversait le terrain d'un coup.
- **Écart d'accélération plus marqué** y écrit `sar edx,1`. Le repère est le sprint balle au pied (`0x4C20C4`), qui donne aussi l'adresse de la stat.

### Virages francs en sprint

Mesuré avec `pes6r turnlog` (une ligne à chaque changement des champs du porteur du ballon) et des points d'arrêt d'exécution :

- En course balle au pied, une foulée dure environ 26 images (0,46 s, animations 65/66), avec une touche vers l'image 7 ; la nouvelle direction du stick n'est prise qu'à la foulée suivante. En sprint, une foulée dure environ 0,67 s (animations 29/30 = `0x1D`/`0x1E`). Un virage de plus de 112° passe par une animation de virage (552, environ 0,65 s).
- La foulée de sprint démarre dans `0x4E2830` (appelée depuis `0x5B7954` quand le bouton de sprint est tenu, `0x4DCBB0` sinon). Elle compare la direction du stick (`0x4B3010`) à l'orientation `+0x26`, en 256e de tour : en dessous de 8 rien ne se passe, à partir de `0x50` (112°) c'est l'animation de virage. Sinon :
  - la **direction du joueur** tourne d'un pas fixe : animation `0x1E` avec 15° (`push 0Fh` en `0x4E299D`) du côté du bon pied, `0x1D` avec 25° (`push 19h` en `0x4E2A51`) de l'autre, via `0x4F3820`, qui passe dans le code protégé ; les deux branches se rejoignent en `0x4E2A7E` avec la nouvelle direction dans `di`, écrite dans `+0x2C` en `0x4E2FA7` ;
  - la **direction du ballon** est calculée en `0x4E2AFF` : `0x4970F0` (tourner une direction vers une autre d'un angle maximum) depuis l'orientation arrondie à 22,5° (`(+0x26 + 0x800) & 0xF000`) vers le stick, d'au plus `0x1000` (`push 1000h` en `0x4E2AE6`). Relevé à la touche (appel du relais en `0x5AFB3B`, direction prise dans `[joueur+0x30]`) : `0xA000`, `0xB000`, `0xC000`… exactement un cran de 22,5° par touche, quoi que demande le stick. **C'est ce cran qui fait tourner « par paliers ».**
- Le joueur court ensuite après le ballon : `0x5AFD45` oriente `+0x2C` à chaque image vers la position prévue du ballon.
- **Virages plus francs en sprint** remplace le `push 1000h` par le virage maximum du porteur : 22,5° jusqu'à 65 en précision de dribble (celui du jeu), le maximum choisi (90° par défaut) à partir de 95, proportionnellement entre les deux. Une touche qui tourne le ballon de plus de 22,5° par rapport à sa direction actuelle (`[ballon+0x58]`) tourne aussi la course (`+0x28`) et l'orientation (`+0x26`, `+0x42`) du joueur vers la direction de la touche, aux deux touches de sprint : `0x5AFB32` (animation 29) et `0x5AFE5C` (animation 30, l'autre pied : `xor ecx,ecx ; mov cx,[edi+0x30] ; add esp,0Ch` avant l'appel du relais). Jusqu'à la 1.18, seule la première était branchée, avec à la place une perte de vitesse jusqu'à 40 % : à la touche de l'autre pied, le joueur finissait sa foulée tout droit et ne tournait qu'à la suivante, environ 300 ms trop tard. Un ralentissement du ballon et du joueur à partir de 45° a été essayé (30 %, puis 15 % à 90°) et abandonné avec le point suivant. Imposer l'orientation à la touche fait sauter l'animation : la foulée de sprint confie donc les virages à partir de 45° à la foulée de dribble normale (`cmp ax,50h` en `0x4E287C` → `20h`), qui joue l'animation de virage : validé en jeu.
- Avec ces touches plus larges, le joueur s'éloignait du ballon : relevé avec `pes6r driftlog` (positions du porteur et du ballon à chaque image), une touche de 45° envoie le ballon tout de suite alors que la course `+0x28` ne tourne que d'environ 15° en une demi-seconde, et l'écart passe de 100 à 500-700 jusqu'à l'animation de virage suivante. Un point d'arrêt en écriture sur `+0x28` du porteur montre pourquoi : en course balle au pied, `0x497140` tourne l'orientation vers sa cible d'au plus `0x100` (1,4°) par image (`push 100h` en `0x4C2088`, puis `+0x26`, `+0x28`, `+0x42` sont écrits ; `add ecx,100h` en `0x4C25C9` dans un autre chemin). **Le joueur tourne plus vite avec le ballon** relève les deux pas (22° par image par défaut). Testé en mémoire à 4, 8 et 16 fois celui du jeu : 16 semblait le meilleur. Un autre chemin balle au pied tourne de `0x300` (`push 300h` en `0x4C214A`) ; il est laissé tel quel.
- Essayé puis abandonné : relever le `push 2Dh` (45°) d'autres foulées (`0x4E1AAF`, `0x4E2231`, `0x4E2BF1`, `0x4E2D70` : elles ne servent jamais en dribble) ; relever les pas de 15° et 25° (sans effet : le code protégé les ignore) ; forcer la direction du joueur sur celle du stick (`0x4E2A7E` : le corps tourne, pas le ballon) ou sur celle de la touche (`0x4E2B04` : l'animation saute, et avec le changement du ballon le joueur ne tourne plus).

### Zone morte du stick

Mesuré avec `pes6r turnlog` en dribblant vers la droite et en bougeant légèrement le stick vers le haut et le bas, puis avec des points d'arrêt en écriture :

- L'angle du stick (`0x1016680` + manette × 4) ne prend jamais de valeur à moins d'environ 16° de 0°, 90°, 180° ou 270° : il reste pile sur l'axe, puis saute à 16-17°. Le joueur continue donc sur une ligne, quoi que fasse le code des directions libres avec l'angle.
- L'angle est écrit en `0x4B23E7` à partir de 4 octets d'axe par manette (`0x3BCEE51`…, un octet par demi-axe), par une arctangente ; là, les deux axes sous `0x19` donnent la zone morte centrale.
- Ces octets viennent du lecteur de manette `0x8B7D63`… : chaque axe brut (0-255, centre 128) est mis à zéro quand il est à `0x25` (37) ou moins du centre (`cmp eax,25h` en `0x8B7D83`, `0x8B7DAA`, `0x8B7DD1`, `0x8B7DFC`), sans remise à l'échelle. C'est une zone morte de 29 % par axe, d'où les 16° (arctan(37/127)).
- **Zone morte du stick analogique réduite** abaisse les 4 limites (18 % = 23 par défaut). Validé en jeu : un léger mouvement du stick oriente le joueur. D'abord 6 % (8), puis 18 % après des matchs : à 6 % le stick était trop sensible (10 % et 14 % essayés aussi). Elle vaut pour les deux sticks (stick droit : passes manuelles).
- Visées des coups de pied arrêtés (#20) : l'attente de corner (`0x58EFE1`, état 47) et celle de coup franc (`0x58AEF6`) tournent la visée `[joueur+0xA0]` de `0x40` par image tant que le bit droite (`0x80`) ou gauche (`0x20`) du stick dans `[joueur+0xB0]` est mis, puis la gardent à moins de `0xD00` de l'orientation (`0x4970F0`). La poussée est ignorée : avec la zone morte réduite, un léger contact la faisait tourner à pleine vitesse. Un crochet ignore une force (`[joueur+0x101]`, 0-8, recopiée de la table du stick par `0x4B2C02` / `0x4B2DE0`) sous 3 et tourne du carré de la force. Validé en jeu : seuils 2, 3 et 4 essayés, 3 le meilleur ; le carré plus agréable que le linéaire. L'attente de 6 mètres (état 45, `0x598640`) a le même code deux fois (`0x5986FC`, `0x598B5B`) avec un pas de `0x80` et une amplitude de `0x1000` : crochetée de la même façon, le carré doublé. Validé en jeu. Une autre copie dans la section protégée (`0x4483609`) tourne la visée avec des boutons (bits `0x400` / `0x800`), pas le stick. La table du stick ne contient que le stick analogique (l'écrivain `0x4B2300` lit les axes ; sa zone centrale efface aussi les bits) : une force de 0 avec un bit de direction, c'est la croix, et les crochets gardent alors le pas entier. Placement du ballon à l'entraînement aux coups francs (`0x4AF58E`) : les mots de position `0x3BCE926` / `0x3BCE928` (recopiés dans le ballon en `0x4AF6B0`) avancent de `0x40` par image pour chaque bit de direction (`0x80` −x, `0x20` +x, `0x10` −z, `0x40` +z), à partir de `edi` = −0x40 et de deux `add word [..],0x40`. Un crochet met dans `edi` l'opposé du pas du stick le plus poussé des 8 manettes, et les deux `add` deviennent `sub word [..],di`. Validé en jeu. La copie de l'angle du stick dans le joueur (`+0xFE`) n'est lue qu'à la frappe (`0x5E58E2`). Trouvé par des points d'arrêt en lecture sur 4 champs du tireur à la fois ; `0x3BE0014`, trouvé par un balayage de la mémoire, était une image d'animation.
- Essayé avant, sans effet tant que l'angle était aplati : supprimer l'arrondi à 22,5° en `0x4E2AF3` (touche de sprint) et `0x4DCE5D` (foulée de course : l'angle du stick), et abaisser les zones mortes des foulées (`0x4E2870` : 8, `0x4DCE73` : `0x18`, `0x4DCEEE` : `0x0E`, en 256e de tour).
- Détacher le débogueur (`pes6watch.ps1`) du jeu l'a fermé.

### Passes en hauteur et centres

Sonde de frappe avec étiquettes (avec bips, pavé numérique), puis points d'arrêt et lecture du code :

- Une **passe en hauteur** (rond au milieu du terrain) est frappée depuis `0x56A585`, le passeur dans l'état 8 ; un **centre** (rond depuis une aile ou près de la surface) depuis `0x587C90`, dans l'état 9. Une autre frappe de l'état 8 vient de `0x50A420`.
- La passe en hauteur est décidée par `0x5697B0` (appelée depuis le code de l'état autour de `0x56DA00`) : le **receveur** est l'octet `+0x48` du passeur, le **point visé** `+0x9C` / `+0xA0`, le genre `+0x4C`. `0x56A260`… calcule ensuite la frappe : vitesse tirée de la stat de puissance de frappe (`0x3BCF5B7`) et de la hauteur, plafonnée selon l'animation.
- Le receveur est choisi par **`0x569020`** (passeur, direction du stick lue par `0x4B2F70`, qui prend l'angle précis du stick analogique) : la **jauge de passe** `+0x4D` (0 à `0x80`, plafonnée en `0x569821`) donne une **distance voulue** `0x800 + jauge / 8 × 0x380` (au moins `0xE00`) ; un coéquipier n'est gardé qu'à moins de `0x700` de cette distance et, pour un humain, à moins de **45°** du stick (`cmp word [esp+0x1C],0x20` en `0x56938F`) ; celui qui a le plus petit (écart d'angle + 1) × écart de distance l'emporte. Si personne n'est gardé, la passe part dans la direction du stick avec la force de la jauge. Juste avant la frappe, `0x45FFE0` revérifie le receveur dans le code protégé.
- **Passes en hauteur : l'appui règle la distance, le stick la direction** fait croître la distance voulue avec le carré de la jauge (`0x800 + jauge² × 7 / 8` : le même maximum, environ 5600 au lieu de 9200 à mi-jauge), et resserre le cône à **22,5°**. Validé en jeu.
- **Passes en profondeur** : le point visé (passeur `+0x9C` / `+0xA0`, trouvé avec un point d'arrêt d'écriture pendant une passe en profondeur) est placé en `0x4D0AF8`–`0x4D0B35` : point de départ + direction × **avance** (passeur `+0xA8`), l'avance étant construite d'après l'angle et la course du receveur (`0x4D0AC4`). Le ballon allait au bon receveur mais loin devant lui, et en touche sur les ailes : le garde-fou de la ligne de touche en `0x4D0B83` (ramène le point de 1024 vers l'intérieur) ne joue que pour certains genres de passe, et au hasard selon une stat du passeur. **Passes en profondeur moins loin devant le receveur** réduit l'avance là où elle est lue (60 % par défaut, F9). Validé en entraînement libre et en match.
- Abandonné : réduire la vitesse des frappes des états 8 et 9 (70 %, F9). Le ballon tombait avant le receveur choisi par le jeu, si bien que la visée semblait aléatoire, et les centres (état 9) étaient déréglés aussi.

### Pressing et tacles

Mesuré avec un relevé du joueur contrôlé par l'humain (boutons tenus `+0xB0`) face au porteur, et des points d'arrêt d'exécution ou d'écriture :

- Presser avec la croix met le joueur dans l'état 15. Le code du pressing (`0x572A90`…) cherche un **point d'interception** sur la trajectoire prévue du ballon, l'enregistre dans `[ballon+0x1454]` / `[ballon+0x145C]` (`0x572B55`) et court vers lui (direction en `0x572B8A`, passée aux fonctions de déplacement ; `0x49030F` tourne `+0x26` d'au plus 22,5° par image ; la position avance selon `+0x28` en `0x496D10`). La course est précise, pas arrondie à 8 directions.
- Cette course amène le joueur à côté du porteur, surtout entre 340 et 500 unités. Viser le porteur à la place le faisait traîner derrière (distance moyenne 2445 au lieu de 440, jamais sous 300) : la course du jeu est la bonne.
- La **tentative de prise de balle** `0x461DC0` (joueur, distance, `0x50`) n'est demandée qu'à moins de 160 unités du ballon (`cmp edi,0A0h` en `0x572AE8`), et la tentative elle-même, pour la plupart des animations, ne se poursuit qu'à moins de vitesse / 128 + 2 × `0x50` (environ 210 en course). Le presseur n'essayait donc presque jamais : 1 tentative en 15 s de possession adverse ; 17 demandes pour 1 acceptée, une fois la portée seule relevée.
- **Au pressing, les joueurs tentent de prendre le ballon de plus loin** multiplie les deux (180 % par défaut : portée 288, paramètre 127, le maximum d'un octet signé). Mesuré sur 40 s : le presseur vient maintenant à moins de 300 du porteur (au plus près 116 au lieu de 337). Les deux équipes sont concernées.
- Pas en cause : l'option d'écartement en largeur (le presseur n'est pas dans l'état 7), vérifié en coupant l'écartement en mémoire.

### Ligne défensive

Mesuré avec un enregistreur des 22 joueurs (positions, mode, mot de la ligne) et lu dans Ghidra (exe importé sans analyse, fonctions décompilées depuis leur entrée) :

- Sans le ballon, les quatre défenseurs étaient au bord de leur surface (environ 3700 de la ligne de but) avec le ballon juste dans leur moitié, et n'atteignaient jamais la ligne médiane avec le ballon dans le dernier tiers adverse.
- L'aiguillage du placement `0x528580` choisit selon le mode du joueur `+0x1DD` (`0x24` → `0x60CB40`, `0x27` → `0x60CD50`, marquage, `8` → `0x619C80`…). Chaque routine construit une cible de base dans les globales `0x3BCDCBC` (x) / `0x3BCDCC0` (z), avec le sens du camp dans `0x3BCDCC4` (`0x642A90` en défense, `0x642540` en attaque), l'ajuste et appelle la fonction de cible `0x4C9DC0`.
- La **ligne défensive** est un mot par camp, `0x3BCDCAE` + camp × 2 (camp = `+0x13` du joueur), calculé par `0x5E1BD0` à partir du x lissé du ballon (`0x3BCDCB2`) : ballon dans l'autre moitié → la moitié de sa distance au centre − `0xA00` (`sub eax,0A00h` en `0x5E1FC5`), gardée 4096 derrière le ballon ; ballon dans la moitié du camp → −`0xA00` − la moitié de cette distance (`mov edx,0FFFFF600h` en `0x5E1E66`), gardée 4096 − distance / 4 derrière le ballon (768 au moins). Puis le niveau attaque/défense `[0x3BE0A6E + équipe]` (0 à 4) : −3072, −1024, 0, +512, +1024 ; puis −2000 (`0xB8C10C`) ; puis −(100 − v) × 40, v étant une valeur d'équipe de 0 à 100 renvoyée par `0x531DA0` (un saut dans la protection ; environ 30 pour l'équipe mesurée). Le résultat est borné à ±11929.
- **Ligne défensive plus haute** réduit les deux `0xA00`. Mis à zéro en mémoire pendant un match, le mot de la ligne est monté d'environ 2000 (ballon dans la moitié du camp près du centre : −7503 → −5445 depuis le centre ; juste dans l'autre moitié : −5401 → −3501), et les quatre défenseurs de 0 à 1500 selon la zone (75 s avant, 75 s après) : ils se tenaient déjà environ 1400 au-dessus du mot, qui ne sert que de plancher (`0x60CB40`, et la fin de `0x642A90`). Mieux ressenti en match.
- Le `+0x12` d'un joueur n'est pas un numéro d'équipe stable (il a changé entre deux relevés du même match) ; les ids 1–11 et 12–22 sont les deux équipes. Le poste est `+0x10` (1–7 défenseurs, 8–`0x17` milieux, `0x18` et plus attaquants).
- Un point d'arrêt d'exécution tenu 12 secondes sur `0x4C9DC0` a été suivi, deux minutes plus tard, d'un plantage du jeu dans le pilote graphique. Le jeu a planté de la même façon (`atidxx32.dll`, violation d'accès) le lendemain moins d'une minute après son lancement, dans les menus, alors que seules des lectures simples de la mémoire tournaient : c'est le pilote qui le fait planter, pas le débogueur.

### Courses vers l'avant en attaque

Lu dans Ghidra et mesuré dans des matches ordi contre ordi sans personne au jeu (`pes6r ab` avec `pes6r positions`, puis `pes6r placement`), le 16 novembre 2026 :

- Quand leur camp attaque (porteur dans la moitié adverse), les coéquipiers sont surtout en **mode 8** (`0x619C80`) : les défenseurs 65 % du temps, les milieux 33 %, les attaquants 49 %, à 24 à 39 m du porteur. Le mode 8 les envoie à leur place de formation (`0x642540`), gardée quelques mètres derrière la dernière ligne adverse (`0x3BE0A10` + autre camp × 4 : 256 à 1024 unités selon le type du joueur, moins avec une stat d'attaque élevée), et `0x5EB4C0` tient la cible à `0x800` (environ 9 m) en travers du coéquipier le plus proche et du porteur. Les modes 1, 2 et 3 sont les arrêts de jeu (`0x3BE0AA4`), pas le jeu courant.
- Un joueur quitte une course par `0x6134C0` (qui se poursuit en `0x6134CD` à travers la protection) : elle libère son emplacement de course et le remet en mode 8 (son camp a le ballon, `0x3BE09E7` + équipe = 0) ou `0x24`. La fonction qui pose le mode est `0x4CA9D0` (joueur, mode).
- **Chaque course n'a qu'un emplacement** : les modes 9 (`0x3BCDCA1`) et `0x0A` (`0x3BCDCA2`) sont les débordements des arrières latéraux, un par aile, choisis par `0x61E820` ; le mode `0x0F`, la course au-delà de la ligne adverse (`0x61A100`), a un octet par camp (`0x3BCDCA9` + camp), choisi par `0x61DAC0`. Un seul joueur par équipe fait donc cette course à la fois.
- `0x61DAC0` choisit le coureur du mode `0x0F` ainsi : rien tant que le compte de `0x496C20` atteint max(niveau d'attaque, 2) (`0x61DB2D` ; niveau d'attaque `0x3BE0A6E` + équipe) ; un porteur défenseur ou milieu défensif peut être choisi lui-même (il continue sa course après sa passe) ; sinon les coéquipiers en mode 8, du côté du ballon (ou une fois sur deux), dont la flèche de formation `+0x1DC` le permet, selon le poste (table en `0x61E62C`) : les défenseurs centraux seulement au niveau d'attaque 4, les latéraux à partir du niveau 2, les milieux défensifs à partir du niveau 3 et jamais quand le porteur en est un aussi, les autres milieux (postes 15 à 23) sur un test de stats (`+0x1A`, réactivité) à tout niveau ; **les attaquants (postes 24 et plus) ne sont jamais choisis**. Un dernier tirage (1 sur 10, 8 ou 4, ou aucun, selon les stats d'attaque et de réactivité, `0x61E460`) termine. `0x483EF0` est le nombre aléatoire inférieur à son argument, `0x483E10` (stat, plancher) ramène une stat au-dessus d'un plancher à 0–100.
- Abandonné : `runs-more` (plafond du compte porté à 4 et dernier tirage retiré). A/B sur 4 × 4 minutes chacun : milieux en mode `0x0F` quand leur camp attaque 8 % → 7 %, défenseurs 3 % → moins de 3 % ; relâchements gardés par un coéquipier 85 % → 81 %. Ces deux freins ne sont pas ce qui limite les courses. Avec 8 % pour chacun des 4 ou 5 milieux et 3 % pour chaque défenseur, l'emplacement unique est déjà pris près de la moitié du temps d'attaque : pour plus de courses il faut un second coureur (l'octet de l'emplacement est lu à une dizaine d'endroits) ou des courses pour les attaquants, pas des choix plus faciles.
- Les mêmes matches tels quels (sans correctif) : 1,4 coéquipier à moins de 15 m du porteur, 2,6 devant le ballon, 85 % des relâchements gardés, une course tournée 0,25 s (temps réel, jeu à ×2) après le changement de camp du ballon, 17 à 20 % des joueurs sans ballon en mouvement regardant à 90° ou plus de leur course.
- Les autres modes de placement sans ballon de l'état 7 (les journaux affichent le mode en décimal : 36 = `0x24`, 39 = `0x27`, 29 = `0x1D`, 53 = `0x35`), lus dans Ghidra le 2026-11-16 : le **7** (`0x615440`) est le mode « tenir sa place » général, une routine pour les deux phases : sous-mode `+0x1DE` 2/3 quand son camp défend (`base_target_defending`, un défenseur (rôle sous 10) reculé d'une constante, les autres bornés par la ligne de hors-jeu adverse), 4/5 quand il attaque (`base_target_attacking`, rôles au-dessus de 9 avancés, tenus sous la ligne de hors-jeu adverse), 6 = le porteur lui-même (il quitte le mode et vise où il se trouve) ; 2/4 tant qu'il est loin de la cible, 3/5 une fois près. Le **`0x1D`** (`0x616B10`) est un placement d'attaque en couloirs fixes (`base_target_attacking`, puis x gardé près de la ligne de hors-jeu adverse et z réglé sur une constante de couloir selon le rôle : rôles 1-2 et les autres devant un seuil couloir 2, rôles 6-7 couloir 3, rôle 8 couloir 4, rôle 9 couloir 5, sous-mode 6 = centré, z 0) ; il se quitte dès que son camp n'a plus le ballon. Le **`0x35`** (`0x60B530`) est un rôle de couverture à place unique : seul le joueur dont l'id est dans `0x3BCDC45` l'exécute ; il se tient sur le mot de ligne défensive (+ la ligne de hors-jeu) et son z suit le ballon (z 0 quand le ballon est loin sur le côté), puis il le quitte (une couverture de type libéro). Ce qui déclenche les modes 7, `0x1D` et `0x35` (les appelants du setter `0x4CA9D0`) n'est pas encore lu.

### Allure sans ballon face aux matches réels

`pes6r compare` sur les matches ordi contre ordi du 16 novembre 2026 : les joueurs sans ballon vont à 3,3 à 4,0 m/s en médiane contre 2,0 à 2,1 dans les deux matches réels, la moitié environ au-dessus de 4 m/s contre 10 à 15 %, et 9 à 14 % marchent (1–2 m/s) contre 30 à 37 % ; 8 % des courses font demi-tour en moins d'une seconde contre aucune. Il y a déjà plus d'appels en profondeur à la fois (0,6 à 0,8) que dans les matches réels (0,2 à 0,4).

- La vitesse de course `+0x1C` vaut environ 1100 unités par m/s (8000 : 7,1 m/s). Elle est écrite par `0x48FEF0` (pas vers une vitesse dans EBX : `0x49001C`…) et, le plus souvent, par le code en `0x4D34F5`…`0x4D3618`, qui tient la vitesse visée dans EDI et la pose (`0x4D358A`) ou s'en rapproche d'un pas (`0x4D35FD`, `0x4D3612`).
- `pace08`, première version : `+0x1C` plafonné à `0x600` + |dx| + |dz| jusqu'à la cible dans `mode08_positioning` (`0x619D77`). Pas d'effet réel (joueurs rapides du camp qui a le ballon 24 % → 18 %, médiane 3,3 → 3,1 m/s) : la moitié des relevés en mode 8 restaient au-dessus du plafond, le code de déplacement réécrit la vitesse ensuite.
- `pace08`, seconde version : le même plafond sur EDI. Un crochet en `0x4D34F3` a fait planter le jeu deux fois (`0x4D34D0` saute en `0x4D34F5`, au milieu du crochet ; et une adresse de retour fausse la première fois). Le crochet est maintenant en `0x4D34F5` ; il a tourné 45 s en match sans plantage, **mais il n'a pas été mesuré**. Avant de poser un crochet, chercher les sauts qui arrivent dans les octets remplacés.
- `pace08`, mesuré (France contre Angleterre, 3 × 240 s, `startmatch` puis `ab`) : aucun effet, médiane hors ballon 3,4 m/s sans le correctif, 4,1 avec (matches réels 2,0). Il ne couvrait que le mode 8 (environ 17 % des échantillons hors ballon ; l'état 7 regroupe les modes 8, 36, 39, 7, 29, 53 pour environ 52 %) et son plafond laissait toute la distance à la cible s'y ajouter. L'A/B s'est terminé par un plantage du jeu (`0x80000003` en `0x5E2920`) : `PatchSet` remettait les 0xCC de la zone de code avant le saut qui y mène ; il retire désormais dans l'ordre inverse de la liste (zone en premier dans `patches.json`, saut en dernier). `pace09` (tous les modes de l'état 7, plafond `0x600` + distance / 4) était l'essai suivant, voir plus bas.
- `pace11` (le plafond de `pace10` avec distance / 2, France contre Angleterre, 3 × 240 s) : médiane hors ballon 3,8 -> 2,1 m/s (réel 2,0-2,1), course 22 -> 6 %, sprints 27 -> 2 %, marche 13 -> 31 %, mais les courses en profondeur tombent de 0,81 à 0,07 par instant (réel 0,23-0,36) : le plafond retenait aussi les modes 9, `0x0A` et `0x0F`. **`pace12`** les laisse tranquilles (mode - 9 <= 1 ou mode - 9 == 6, testé dans CL) : médiane 4,0 -> 2,1, course 25 -> 6 %, sprints 26 -> 6 % (réel 2-3 %), marche 11 -> 27 % (réel 35-37 %), courses en profondeur 0,59 -> 0,17 (réel 0,23-0,36), coéquipiers qui viennent vers le ballon 2,03 -> 1,02 (réel 1,06-1,11), courses retournées 7 -> 4 % (réel 0), relâchements par minute 27 -> 25 (réel 34-40), duels gagnés 2,3 -> 1,9 (réel 0,7-0,8), coéquipiers devant le ballon 2,2 -> 1,9 (réel 3,6-3,8 : pire), délai de réaction inchangé 0,5-0,6 s (réel 1,5 s). Pas encore essayé au ressenti.
- Retour de l'utilisateur après deux matches avec `pace12` (2026-11-16) : il a aimé, « ça change beaucoup le gameplay, c'était l'idée », mais le joueur humain peut sprinter tout le match (surtout pour récupérer le ballon) et l'ordinateur devrait sprinter plus. `pace13` (le plafond `0x600` + distance, sans diviser par deux ; un joueur dont la place est à plus de 17 m environ peut sprinter) : médiane hors ballon 3,8 -> 2,6 m/s, sprints 25 -> 10 % (réel 2-3 % ; `pace12` 6 %), course 23 -> 15 %, marche 13 -> 18 %, courses en profondeur 0,65 -> 0,26 (réel 0,23-0,36), relâchements par minute 25 -> 26. Le match de cet essai a été lancé à la main (Brésil contre France : `startmatch` s'est arrêté à l'étape de la manette, le jeu n'avait pas pris le focus). Idée si cela ne suffit pas : un plafond qui se desserre pour le camp qui vient de perdre le ballon.
- **Dans le patcher** (2026-11-16) : l'utilisateur a demandé que l'allure soit réglable et que ses valeurs soient celles du profil Pro, pour que d'autres testent d'eux-mêmes. L'option **Allure sans ballon plus lente** (`PatchOptions.OffBallPace`, de 25 à 400 %, 100 par défaut = `pace13` ; profils Normal 75, Pro 100, Hardcore 150) écrit deux zones de code dans l'exe, repérées par signature (`ExeLocator.LocateOffBallPace` : le site de l'animation `8B 4E 04 33 D2 66 8B 51 30 C7 44 24 18 00 04 00 00` et l'entrée `jmp [pointeur]` de `move_speed_towards`, uniques toutes les deux) : le plafond est 0x600 + distance x pourcentage / 100, fait par `imul ecx,ecx,pourcentage x 256 / 100 ; shr ecx,8`. Un test compare les deux zones octet par octet avec les codages qui ont tourné en match. `pes6r pace -Percent N` écrit la même option dans le jeu en marche (le même code `CodeCaves`, l'espace libre trouvé dans la mémoire du jeu ; `-Revert` la retire) : dans un match sans / avec (France contre Angleterre, octets construits par le patcher) : médiane 3,6 -> 2,7 m/s, sprints 25 -> 12 %, courses en profondeur 0,62 -> 0,16, pas de plantage.
- **Tireur de coup franc qui rate le ballon (2026-11-17)**, signalé par l'utilisateur en testant l'option d'allure sans ballon en direct dans un match : le tireur rate le ballon, « hyper frustrant ». Le plafond touche tout joueur à l'état de placement (7) qui n'est pas sur une course, et à une remise en jeu les coéquipiers et le mur sont à cet état, dans les modes d'arrêt de jeu 1 à 6 (les tireurs eux-mêmes étaient aux états 5, 44, 45 et 48 dans les journaux, donc pas plafonnés directement ; la cause n'est pas prouvée, seulement que le plafond n'avait rien à faire dans une remise en jeu). Le plafond ne s'applique plus qu'en jeu courant : les deux zones de code testent l'octet d'état de remise en jeu `0x3BE0AA4` (`ExeLocator` le trouve depuis `movzx eax,byte [état] ; sub eax,2 ; jz ... ; sub eax,5 ; jz ...` de l'aiguillage) et laissent au jeu un corner, un coup franc, une touche, un but et la mi-temps. Si le tireur rate encore le ballon avec le plafond en jeu courant seulement, la cause est ailleurs (une option plus ancienne comme les coups francs ou les visées de la zone morte du stick).
- **Bug du tireur de coup franc, issue (2026-11-17)** : avec l'exe reconstruit par le patcher avec le plafond en jeu courant seulement, l'utilisateur a joué et plus aucun tireur, humain ou ordinateur, n'a raté le ballon : la cause était le plafond qui ralentissait les joueurs pendant les remises en jeu. Les états de remise en jeu (octet `0x3BE0AA4`) : 0 en jeu, **1 coup franc (confirmé en l'écrivant en direct), 2 touche**, 3 et 6 à la ligne de but, 5 un état intermédiaire, 7 but / mi-temps / fin de match ; les compteurs de scènes en `0x10104C0` (niveau 1 : `01` en jeu, `0C` -> `0D` -> `0E` pendant un arrêt). Écrire le niveau 1 = `0C`, le niveau 2 = 0 et l'état 1 dans un match en cours fait faire un coup franc au jeu, mais l'a fait planter 3 fois sur 5 (un joueur ou un objet absent dans le code du jeu) : trop fragile pour une commande. Nouveaux outils de recherche : `pes6r memlog` (changements de plages de mémoire), `pes6r snapwatch` (photos au début d'un arrêt de jeu), `snapdiff -Skip`.
- `speed-x4` fige le jeu : il s'est figé 21 s après la pose du patch dans un match (le fil principal boucle en `0x4BBB62`-`0x4BC6ED`, la prédiction d'interception du ballon, qui lit `[ballon+0x1458]`) ; aucun code des patchs n'est en cause. Rester à ×2 ; le ×3 n'est pas essayé.
- Le jeu démarre sur son écran de langue à chaque lancement (l'utilisateur l'a toujours eu) : `pes6r startmatch` choisit Français, passe les menus et lance un match ordinateur contre ordinateur, France contre Angleterre par défaut (les équipes du premier groupe, Europe A, sont cartographiées ; pas encore les autres groupes). Utiliser de très bonnes équipes pour tout match sans surveillance : les reproches visent les bons joueurs.
- **`move_speed_towards` `0x48FEF0`** (joueur, vitesse visée, pas ; cdecl ; 82 appelants dans le code des actions `0x490000`-`0x4C3E00`) est l'autre écrivain de `+0x1C` (le corps est dans la protection, `jmp [0x44603DC]` vers un bout obscurci en `0x44B6A20`, non décodé). `pace09` (tous les modes de l'état 7, plafond `0x600` + distance / 4 sur EDI en `0x4D34F5`) a changé le jogging en marche mais pas les sprints (23 -> 24 %). `pace10` ajoute un plafond sur l'argument de vitesse visée à l'entrée de `0x48FEF0` (zone `0x5E2980`) : médiane hors ballon 3,2 -> 1,9 m/s (réel 2,0), sprints 23 -> 1 % (réel 2-3 %), marche 10 -> 32-35 % (réel 30-37 %), duels gagnés 1,8 -> 0,8 par minute (réel 0,7-0,8). Trop fort : course à 4-5,5 m/s 1 % (réel 7-9 %), 22 relâchements par minute (réel 34-40), demi-tours réussis 55-64 % (réel 68-71 %). À desserrer (distance / 2 ou / 3).
- **État de remise en jeu** `0x3BE0AA4` (`GameLayout.RestartState`) : 0 en jeu, 1 arrêts de jeu annoncés, 2 coup franc, 3 corner (le plus probable), 5 le gardien tient le ballon, 6 touche, 7 les longues pauses sans HUD : séquence de but, mi-temps (le « Résumé » des buts tourne en boucle jusqu'à Échap puis Entrée) et fin de match. `pes6r ab` appuyait sur Échap à tout arrêt de 4 s, ce qui ouvrait le menu PAUSE aux corners ; il n'appuie plus qu'après 4 s à la valeur 7, ou après 15 s d'immobilité réelle à 0, et jamais de 1 à 6 (vérifié le 2026-11-16 : 7 groupes de touches, tous dans un 7). L'indicateur `lost` (gardiens loin de leurs buts) peut être vrai en plein jeu : il ne compte qu'à 7.

### Touches de test

F2 à F9 changent une valeur en match et l'affichent par Kitserver, comme F1. Leur mise en place est appelée depuis l'entrée de la fonction de frappe jusqu'à ce qu'elle soit faite ; leur état est dans les 32 octets qui précèdent ceux du dribble resserré (`0xBC3FD0`). F2 à F5 avancent de 10 %, F6 de 25 %, F7 de 15°, F8 de 20 %, F9 de 10 % (les textes de chaque palier sont dans des zones libres à part).

---

## 4. Carte mémoire (version de référence)

| Donnée | Adresse / champ |
|---|---|
| Joueurs | `0x3BDC980` + id × `0x240` (id 1 à 22 sur le terrain) |
| Joueur : équipe, poste (0 = gardien) | `+0x12`, `+0x11` |
| Joueur : état, minuterie | `+0x16`, `+0x18` |
| Joueur : vitesse de course, vitesse maximale | `+0x1C`, `+0x58` |
| Joueur : cap, direction demandée | `+0x2C`, `+0x2E` |
| Joueur : cible, position | `+0x6C` / `+0x70`, `+0xE0` / `+0xE8` |
| Joueur : animation | `[+4]` (`+0x2D` pied, `+0x30` animation, `+0x34` image) |
| Joueur : touches tenues, appuyées | `+0xB0`, `+0xB4` |
| Id du porteur du ballon | `0x3BE09CC` |
| Ballon | `[0xBCCE94]` : position `+0x10/+0x14/+0x18`, vitesse verticale `+0x30`, vitesse `+0x50`, direction `+0x58`, effet `+0x6A/+0x6C/+0x74` |
| Stats | `0x3BCF5A8` + (équipe × 32 + poste) × `0x348`, 7 bits par stat (vérifié contre les stats affichées en jeu, 15 novembre 2026) : `+0x0` bit 0 pied fort, puis à partir de `+0x2` dans l'ordre de l'écran du joueur : attaque, défense, équilibre, physique, vitesse max (`+0x6`), accélération (`+0x7`), réaction, agilité, précision de dribble (`+0xA`), vitesse de dribble, précision et vitesse de passe courte, précision et vitesse de passe longue, précision de tir (`+0x10`), puissance, technique de tir (`+0x12`), précision de coup franc, courbe, tête, détente (`+0x16`), une stat non affichée (`+0x17`), technique (`+0x18`), agressivité (`+0x19`) |
| Sticks analogiques | `0x1016680` + manette × 4 : angle (mot), amplitude, bits de direction |
| Animation (`[joueur+4]`) | `+0x28` vitesse de lecture (flottant, tirée du pas d'image global, ou en % par `0x65D9B0`), `+0x30` animation, `+0x34` nombre d'images, `+0x38` temps (flottant) ; avancée par `0x65D870`, lancée par `0x65D7D0` |
| Constantes physiques du ballon | 10 flottants en `0xB8AE68` (voir ci-dessous) |

Constantes physiques du ballon (les 40 octets que changent le BallPhysics Editor de Rechi et le `gameplay.dll` de DonMike), lues par le code de vol du ballon `0x475810` :

| Adresse | Valeur | Rôle | Lue ailleurs |
|---|---|---|---|
| `0xB8AE68` | 1/600 | amortissement d'un ballon qui roule lentement | 1 endroit |
| `0xB8AE6C` | 1/32 | freinage du ballon qui roule | 13 endroits |
| `0xB8AE70` | 188 | gravité, ajoutée à la vitesse verticale à chaque image | 3 endroits, dont la visée des frappes (`0x515909`) |
| `0xB8AE74` | 4,07e-5 | effet Magnus : rotation × vitesse² | non |
| `0xB8AE78` | −376 | en dessous, le ballon cesse de rebondir et roule | non |
| `0xB8AE7C` / `0xB8AE80` / `0xB8AE84` | 184 / −1/256 / 9024 | rebond : sous 9024, 53 à 63 % de la vitesse verticale est gardée (selon `+0x50` du ballon) ; au-dessus, 72 % de l'excédent | peu |
| `0xB8AE88` | 0,8 | vitesse horizontale perdue au rebond | 49 endroits |
| `0xB8AE8C` | −28 | hauteur du sol (rayon du ballon) | 11 endroits |

---

## 5. Outils de recherche

Les scripts de [`scripts`](scripts) ont servi à trouver les correctifs. L'application n'en a pas besoin : le patch lui-même est dans `src/PES6Patcher.Core`.

La plupart vivent maintenant dans un outil console C#, [`Pes6Research`](Pes6Research) (`pes6r.exe`) : un seul module mémoire partagé au lieu d'une copie de `ReadProcessMemory` par script, pas de compilation à chaque lancement, et un échantillonnage plus régulier (100 ms demandées donnent 100 ms, pas 110 à 125). Le compiler une fois avec `dotnet build research/Pes6Research -c Release`, puis lancer `research/Pes6Research/bin/pes6r.exe <commande>` (sans commande : la liste). Les options gardent les noms des scripts. Ce qui n'a besoin ni du jeu ni de ses fichiers (options, hex, patchs sur une fausse mémoire, sections de l'exe, depth et shape) est testé : `dotnet test research/Pes6Research.Tests`. Les commandes, et le script que chacune remplace :

| Commande | Remplace | Rôle |
|---|---|---|
| `pes6r dump -Address 0x3BECC60 [-Length 0x40]` | `pes6dump.ps1` | la mémoire en colonnes int16 / int32 / float (point décimal, quelle que soit la langue de Windows) |
| `pes6r positions [-Seconds 60] [-Out positions.csv] [-Every 100] [-StartDelay 5] [-Patch nom]` | `pes6positions.ps1` | CSV des 22 joueurs et du ballon, avec le rôle de chaque joueur (`+0x10`), son mode de placement (`+0x1DD`), son orientation, sa course et la direction visée, et la hauteur du ballon (`by`, qui croît vers le bas : environ -23 au sol) ; `-Patch` ajoute une colonne A/B indiquant si ce patch est en place (à lancer à côté de `ab`) ; écrit au fur et à mesure, donc Ctrl+C ou un plantage du jeu gardent les échantillons ; pas de ligne quand les joueurs sont illisibles |
| `pes6r patch [-List] [-Apply a,b] [-Revert a,b] [-RevertAll] [-Force]` | `pes6patch.ps1` | modifications nommées de `patches.json`, voir plus bas ; `pes6r ab` et `pes6r scoreprobe` s'en servent |
| `pes6r code -Address 0x8744F0 [-Length 16] [-Hex "1C 03 00 00"]` | `pes6code.ps1` | lire des octets, ou les écrire (mémoire uniquement, code compris) puis les relire |
| `pes6r players` | `pes6players.ps1` | un instantané des 22 joueurs et du ballon |
| `pes6r depth -In positions.csv [-Field p\|t]` | `pes6depth.ps1` | ligne arrière par zone du ballon à partir d'un CSV de `positions` (`t` : les cibles des joueurs) ; écrit `<entrée>-<champ>-teams.csv` avec un point décimal |
| `pes6r placement -In positions.csv` | (nouveau, 15 novembre 2026) | placement sans ballon d'un relevé `positions` : place, rôle et modes de chaque joueur ; centres (coéquipiers et attaquants au point de chute) ; foule autour du porteur et ballons gagnés sur un tacle ; soutien en attaque (coéquipiers proches, devant, qui avancent), ballons lâchés récupérés par un coéquipier, réactions après un changement de possession ; orientation contre course ; sauts de cible par mode ; milieu défensif face à la ligne arrière. Séparé par phase quand le relevé a la colonne `patch`. Les défenseurs, milieux et attaquants du camp qui attaque sont aussi donnés par mode de placement (part, distance au porteur, course vers l'avant). Relever avec `-Every 50` : les déplacements sont mesurés sur 5 relevés |
| `pes6r compare -In a.csv,b.csv [-Speed 2] [-Metrica dossier] [-Games 1,2] [-SaveReference]` | (nouveau, 16 novembre 2026) | des relevés `positions` face à des matches réels, les mêmes mesures en mètres et en secondes de match, une colonne chacun (et une par phase A/B quand le relevé a la colonne `patch`) : vitesses sans ballon, soutien en attaque, foule autour du porteur, forme de l'équipe, passes, tacles, réactions quand le ballon change de camp, centres. Les colonnes réelles sont lues dans [`real-matches.csv`](real-matches.csv) (deux matches des [données d'exemple de Metrica Sports](https://github.com/metrica-sports/sample-data), positions à 25 Hz et événements) ; `-Metrica` les recalcule depuis les fichiers téléchargés et `-SaveReference` les réécrit. Première lecture (match ordi contre ordi, 16 novembre 2026) : les joueurs du jeu se déplacent sans ballon à 3,5-4,2 m/s en médiane contre 2,0 dans les matches réels (au-dessus de 5,5 m/s 24 à 30 % du temps contre 2 à 4 %), 6 à 8 % de leurs courses font demi-tour en moins d'une seconde contre 0 %, ils réagissent à un changement de camp en 0,4 à 0,6 s contre 1,5 s, 2,0 à 2,6 coéquipiers sont devant le ballon contre 3,6 à 3,8, et il y a 19 à 30 relances par minute de possession contre 34 à 40, avec la même part qui arrive à un coéquipier (77 à 85 %) |
| `pes6r findref -Exe PES6.exe.orig -Values 0x3BCDCAE,0x3BE0A6E` | `pes6findref.ps1` | sur le fichier exe : les endroits qui contiennent une valeur 32 bits (0,1 s au lieu de 3,6) |
| `pes6r readva -Exe PES6.exe.orig -Addresses 0xB8C10C,0x5E23FC [-Length 8]` | `pes6readva.ps1` | sur le fichier exe : float, int32 et octets à des adresses virtuelles |
| `pes6r shape [-Seconds 90] [-Every 250] [-Out shape.csv]` | `pes6shape.ps1` | forme de l'équipe sans le ballon, par zone du ballon |
| `pes6r driftlog [-Seconds 30] [-Out drift.csv] [-StartDelay 3]` | `pes6driftlog.ps1` | le porteur et le ballon toutes les ~5 ms tant que le porteur bouge |
| `pes6r turnlog [-Seconds 30] [-Out turn.csv] [-StartDelay 5]` | `pes6turnlog.ps1` | une ligne à chaque changement des champs du porteur ; attend 1 ms entre deux lectures au lieu d'occuper un cœur, et attrape toujours chaque image |
| `pes6r heading -Players 0x3BDD700,0x3BDDB80 [-Seconds 10]` | `pes6heading.ps1` | le cap des joueurs face à l'angle du stick ; 10 mesures par seconde comme demandé (le script en faisait 9) |
| `pes6r stick [-StartDelay 5] [-Hold 3] [-Out stick.txt]` | `pes6stick.ps1` | la mémoire qui suit le stick, guidé par des bips |
| `pes6r ab -Patch defence-line [-Phase 90] [-Cycles 3] [-Settle 10] [-SpeedPatch speed-x2] [-SkipScan 1]` | `pes6ab.ps1` | A/B automatique d'un patch pendant un match ordinateur contre ordinateur à vitesse x2 : ligne arrière par zone du ballon, patch désactivé (A) et activé (B) ; à la fin (Ctrl+C compris) le jeu est remis exactement comme avant, donc un patch appliqué dans l'exe par le patcher le reste, et un mot de ligne que l'octet de côté du gardien ne peut pas indexer donne NaN au lieu d'arrêter le relevé |
| `pes6r scoreprobe [-Seconds 600] [-SkipScan 1]` | `pes6scoreprobe.ps1` | les valeurs 32 bits d'une zone qui ne font que monter de 1, avec la position du ballon (la recherche des compteurs de score) |
| `pes6r calls -Address 0x4C9DC0 [-Seconds 10] [-Out calls.csv] [-NArgs 3]` | `pes6calls.ps1` | débogueur : chaque appel d'une fonction, avec le joueur, les arguments et les appelants |
| `pes6r hits -Addresses 0x404338,0x4043A3 [-Seconds 3]` | `pes6hits.ps1` | débogueur : combien de fois jusqu'à 4 adresses s'exécutent ; appelants, EDI et premier argument de la première |
| `pes6r watch -Address 0x3BECC84 [-Seconds 4] [-Read]` | `pes6watch.ps1` | débogueur : les instructions qui écrivent (ou avec `-Read` utilisent) une adresse |
| `pes6r viewport [-Seconds 3]` | `pes6viewport.ps1` | débogueur : les appels Direct3D `SetViewport` |
| `pes6r window` | `pes6window.ps1` | géométrie et prise en compte du DPI des fenêtres du jeu, en pixels physiques |
| `pes6r capture [-Out shot.png]` | `capture-window.ps1` | capture de la fenêtre principale, même derrière d'autres ; `-Process PES6Patcher` pour l'application |

Les quatre commandes du débogueur partagent un seul module (c'étaient quatre copies), se détachent aussi sur Ctrl+C et s'arrêtent quand le jeu se ferme. Comparées aux scripts pendant un match le 14/11/2026 (mêmes instructions, appelants et colonnes ; le jeu a continué de tourner après 8 sessions courtes). Garder les sessions courtes.

`ab` et `scoreprobe` n'envoient la touche pour passer (Échap, le Start de l'utilisateur) que si la fenêtre du jeu est vraiment passée au premier plan, pour qu'elle n'atterrisse jamais dans une autre fenêtre.

Les commandes guidées par des bips (aigu : début, grave : fin) s'arrêtent proprement sur Ctrl+C et quand le jeu se ferme, en gardant ce qu'elles ont relevé.

Chaque commande accepte `-Process <nom>` pour lire un autre processus que PES6.

| Script | Rôle |
|---|---|
| `DumpAt.java`, `DumpRefs.java`, `ListRefs.java`, `Disasm.java`, `FindDevCalls.java` | scripts Ghidra (mode headless) : décompiler une fonction, lister les références à une adresse, désassembler une plage, trouver les appels d'une méthode du périphérique Direct3D 8 |
| `pes6r code`, `pes6r dump` | lire ou modifier des octets dans le jeu en cours d'exécution (mémoire uniquement) |
| `pes6r patch`, `patches.json` | modifications nommées de la mémoire du jeu en cours : `-List`, `-Apply nom`, `-Revert nom`, `-RevertAll` ; les octets d'origine sont vérifiés d'abord, rien n'est écrit par-dessus un autre patch ou une autre version. Ajouter une entrée à `patches.json` pour chaque idée essayée en mémoire |
| `pes6r players`, `pes6r positions` | en lecture seule (ReadProcessMemory, sans débogueur) : un instantané des 22 joueurs et du ballon, ou un CSV toutes les 100 à 250 ms pendant un match entier |
| `pes6r depth`, `pes6r shape` | à partir de ce CSV (ou en échantillonnant en direct) : hauteur de la ligne arrière par zone du ballon, et forme de l'équipe (défenseurs, milieux, attaquants, longueur, largeur) quand l'adversaire a le ballon |
| `pes6r calls` | point d'arrêt d'exécution sur une fonction : chaque appel avec son appelant et ses arguments. Il attache le débogueur (une cave de trace dans le padding de l'exe, lue avec ReadProcessMemory, l'éviterait ; le jeu plante aussi tout seul dans le pilote graphique, voir la note sur la ligne défensive) |
| `pes6r findref`, `pes6r readva` | travaillent sur le fichier exe (le jeu n'a pas besoin de tourner) : trouver le code qui contient une adresse, lire des constantes à des adresses virtuelles |
| `pes6ghidra.ps1`, `ApplyLabels.java`, `DecompIn.java`, `DecompAt.java`, `../ghidra-labels.csv` | un projet Ghidra persistant de l'exe d'origine, dans un chemin court (`-Import` une fois, environ 40 s ; `-Decompile 5E1BD0,642A90` environ 12 s). Les fonctions créées et nommées y sont enregistrées ; ajouter ce qu'on apprend à `ghidra-labels.csv` puis lancer `-Labels`. `-At` crée la fonction exactement à l'adresse donnée (code atteint par un saut qui sort de la protection : une fonction qui commence par `jmp [0x44xxxxxx]` se poursuit là où mène le pointeur du jeu en cours, `pes6r code -Address 0x44xxxxxx`). Mettre entre apostrophes les adresses qui ressemblent à des nombres (`'61E932'` vaut l'infini pour PowerShell). Le décompilateur perd les branches de certaines tables de sauts : vérifier avec `Disasm.java` (début, fin), qui désassemble aussi ce qui ne l'est pas encore |
| `pes6r watch` | point d'arrêt matériel en écriture ou en lecture sur une adresse : quelles instructions la touchent |
| `pes6r hits` | points d'arrêt matériels en exécution : combien de fois du code s'exécute, avec quels appelants et arguments |
| `pes6r viewport` | relève les appels `SetViewport` (rectangle et appelant), ce qui a mené au découpage des listes |
| `pes6r stick` | relevé mémoire guidé par bips (stick au repos, à droite, en haut), ce qui a mené à l'état de la manette |
| `pes6r heading` | mesure le cap réel des joueurs face à l'angle du stick, avec et sans les directions libres |
| `pes6r turnlog` | relevé du porteur du ballon guidé par des bips (état, animation, image, orientations, vitesse, direction du ballon) : une ligne CSV à chaque changement, qui a montré les crans de virage en sprint |
| `pes6r window`, `pes6r capture` | géométrie et capture de la fenêtre du jeu ou de l'application |
| `make-signatures.ps1` | construit la signature d'un emplacement : les octets qui l'entourent, adresses absolues et cibles d'appel masquées, allongés jusqu'à n'apparaître qu'une fois dans le code |
| `StringRefs.java` | script Ghidra : décompile les fonctions qui utilisent une chaîne (par exemple les noms de versions de Kitserver dans `kload.dll`) |
| `set-resx.ps1` | ajoute, modifie ou supprime des textes dans les ressources anglaises et françaises |

Les options de gameplay ont surtout été trouvées avec des variantes de `pes6r watch` et `pes6r hits` (plusieurs adresses à la fois, registres et pile relevés à chaque passage), et avec des relevés mémoire des joueurs, du ballon et des stats pendant le jeu, ensuite comparés.

### Plusieurs versions de l'exe

Kitserver distingue trois versions du jeu : « PES6 PC » (1.00), « PES6 PC 1.10 » et « WE-PES 2007 PC ». Les patchs communautaires fournissent aussi leurs propres exe. Plutôt qu'une table d'adresses par version, qui demanderait de posséder chaque exe, l'application retrouve les emplacements par signature (`src/PES6Patcher.Core/Patching/ExeLocator.cs`) :

- les signatures sont celles de `make-signatures.ps1`, construites vers l'avant à partir de chaque emplacement ;
- les adresses utilisées par le code ajouté (constantes, fonctions, tables, stats, tableau des joueurs…) sont lues dans le code trouvé, puis vérifiées ;
- les deux appels qui transmettent la largeur à la 3D sont reconnus par le contenu de la fonction appelée, car un troisième appel identique alimente le découpage 2D ;
- les zones libres sont les plages de `CC` d'au moins 80 octets du code, et l'état va dans la fin de `.rdata`, chargée par Windows même au-delà des données du fichier ;
- la formule de la vitesse maximale est cherchée dans toutes les sections exécutables, car elle se trouve dans la section `.rld` de la protection.

Sur l'exe de référence, ce moteur produit octet pour octet la v6 validée en jeu (tests `Build_matches_the_exe_validated_in_game`).

Le décompilateur de Ghidra ne se lance pas depuis un chemin de plus de 260 caractères : installer Ghidra dans un dossier court.

---

## 6. Points techniques de la configuration écran large

- **Fenêtre sans bordure.** `kload.cfg` impose la fenêtre avec `dx.window.x/y/width/height` et `dx.window.style = 0x96000000`. Elle couvre exactement l'écran. Il faut rester en mode fenêtré dans le jeu.
- **Résolution du jeu.** `settings.exe` ne propose pas plus de 1920 × 1080. Elle est donc écrite directement dans `Documents\KONAMI\Pro Evolution Soccer 6\settings.dat` :
  - largeur aux octets 4-5 ;
  - hauteur aux octets 6-7 ;
  - aux octets 2-3, une somme de contrôle : la somme sur 16 bits des octets à partir du 4ᵉ.

  Ne pas rouvrir `settings.exe` après coup : il remettrait 1920 × 1080.
- **Mise à l'échelle Windows.** Avec un affichage à 125 %, le jeu reçoit bien la résolution physique. Le décalage observé au début venait de la fenêtre de 1920 × 1080 et de la résolution du jeu, pas de la mise à l'échelle.
- **Format 32:9.** Les menus n'occupent qu'environ un tiers de la largeur, au centre.

Profils validés en jeu :

| Profil | Demi-largeur 2D | Rendu (kload) | Fenêtre | Résolution du jeu | Format 3D (lodmixer) |
|---|---|---|---|---|---|
| 16:9 | 398 | 2560 × 1440 | normale | 1920 × 1080 | 1.77778 |
| 32:9 | 796 | 5120 × 1440 | sans bordure, 5120 × 1440 en (0, 0) | 5120 × 1440 | 3.55556 |

---

## 7. Installer et revenir en arrière

L'application PES6 Patcher installe ces réglages pour n'importe quelle résolution. Son bouton **Restaurer l'original** remet tous les fichiers tels qu'ils étaient avant sa première utilisation.

---

## 8. Autres fausses pistes

- **Police des menus plus lisse** : passer le filtrage de la texture de la police en linéaire n'a rien changé de visible.
- **Dribble à 360° de l'IA** : les directions de dribble de l'IA sont déjà arrondies (surtout à 45°) au moment où elle les décide. `+0x2C` contient très probablement la direction qu'elle vise ; l'utiliser ne changeait chaque touche que de 2 à 13°, sans gain visible.
- **Moins d'effet sur le ballon** : multiplier la constante Magnus (`0xB8AE74`) de 10 à 150 % avec une touche de test n'a rien changé de perceptible en jeu : ce terme est minuscule à côté de la gravité.

---

## 9. Limites connues

- Les décors des menus ne font que 512 de large : bandes noires sur les côtés.
- Dans le menu principal, une seconde copie de l'image du joueur apparaît dans la bande droite.
- Les directions libres suivent l'angle précis du stick en course et pour la touche de dribble la plus fréquente ; quelques touches moins fréquentes et les gestes spéciaux gardent les directions du jeu.
- Seule la version de référence a été testée en jeu ; les autres sont patchées quand leur code correspond.
