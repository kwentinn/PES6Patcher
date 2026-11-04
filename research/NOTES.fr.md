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
- **Dribble resserré** : à l'entrée du relais, la vitesse des 7 touches de dribble est réduite (× 14/16 en course, × 13/16 en sprint), en proportion de la précision de dribble du joueur (stat `0x3BCF5C0`, celle que le jeu lit pour l'imprécision de direction des touches) : rien jusqu'à 65, la réduction complète à partir de 95.
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
- **Équipes humaines** : à chaque frappe, pour chaque manette dont le stick est poussé, le joueur actif de chaque équipe gagne +1 si sa direction demandée est celle du stick (ou l'opposée), −2 sinon. Une équipe à 32 ou plus (sur 64) est humaine. Les tirs de l'IA ne sont pas touchés.
- **Types de tir** : un tir est l'état 4 du joueur. Son code `0x5BBF90` fait un aiguillage sur `+0x18` (`0x00`…`0x27`, table en `0x5BD858`, cas en `0x5BD800`) ; 17 valeurs sont des types de tir (`0x10`–`0x14`, `0x16`, `0x18`, `0x1B`–`0x1E`, `0x21`–`0x25`, `0x27`), chacune appelant la fonction de tir `0x5B9130` depuis son propre endroit (`0x5BC321` … `0x5BD7BA`) avec sa propre base de puissance (`0x300` à `0xA00`) et ses paramètres. Le type est choisi par le code de décision du tir, de `0x544954` à `0x54980A` (19 endroits qui fixent l'état 4 et `+0x18`), plus `0x579AEB` (type `0x27`) et les coups de pied arrêtés (`0x58C026`, `0x58C323`). Quel type est lequel (tir en course, reprise de volée, retourné…) n'a pas été établi : lu au début d'un tir, `+0x18` contenait une phase (0 ou 1), pas le type.
- La fonction de tir tire beaucoup de nombres aléatoires (`0x483EF0`, `0x483F40`, une quarantaine d'appels entre `0x5B93CB` et `0x5B9E59`) avant l'appel de frappe `0x5BBC9A` ; c'est pourquoi la précision agit sur la direction et la hauteur finales de l'appel de frappe plutôt que sur chaque tirage.
- **Lobs** : un lob est un tir dont le tireur tient le bouton `0x400` (L1) dans `+0xB0` au départ du ballon (tous les lobs relevés ; aucun des autres tirs). Sonde de frappe (`pes6probe.ps1`, branche `kick-probe`) : les lobs classiques utilisent les animations `0xC9`, `0xCA`, `0x244`, `0x245` et partent à une hauteur de 45 à 68, contre 13 à 35 pour les tirs ; les lobs mal placés utilisent d'autres animations (`0xF`, `0xEE`) et partent bas. La précision réduisait toute hauteur au-dessus de 20 comme une erreur, ce qui aplatissait les lobs (la moitié de leur hauteur à 50 %, presque tout à 1 %). Un lob garde maintenant 95 % de sa hauteur et reçoit la puissance des lobs (70 % par défaut) sur sa vitesse. Essayé puis abandonné : reconnaître les lobs à leur hauteur (rate les lobs bas) ; réduire l'argument de puissance à l'entrée de la fonction de tir pour les lobs (irrégulier : la force du lob se décide plus loin).
- Mesuré en jeu avec le réglage à 1 % : les tirs sont « beaucoup plus cadrés » ; ce qui rate encore vient de la visée elle-même (la direction demandée), pas de l'écart aléatoire.
- Fausse piste : la jauge de tir. La puissance est décidée par le type de tir et des formules (par exemple `0x108 + [joueur+0xA4] × 24 / (8142 − 16 × [joueur+0x5C])`), pas par un compteur visible. La saisie humaine est recopiée dans le joueur (`+0xB0`, bit `0x800` = tir) par du code protégé (vers `0x448A73D`) ; l'IA appuie sur le même bit. `[joueur+4]+0x34` est l'image de l'animation, pas un compteur d'appui.

### Joueurs plus écartés

- L'endroit où un joueur se place passe par la **fonction de cible** `0x4C9DC0` (joueur, x, z), appelée par 45 morceaux de l'IA de placement ; son corps continue dans du code protégé. Seuls les joueurs qui se replacent (état `+0x16` = 7) l'utilisent.
- Le code ajouté éloigne la cible du centre des cibles de l'équipe (`+0x6C` / `+0x70` de ses joueurs de champ), en largeur seulement (z). Écarter aussi en longueur (x) faisait reculer les défenseurs près de leur gardien.

### Écart de vitesse

- La vitesse de course est `[joueur+0x1C]`. À chaque image, `0x48FEF0` la rapproche d'une vitesse visée donnée par l'appelant (sprint balle au pied : `0x4C20F9`), avec un temps d'accélération tiré de la stat `0x3BCF5B0`.
- La **vitesse maximale** `[joueur+0x58]` est calculée, et recalculée pendant le match, en `0x447BF52` (section `.rld` de la protection, code en clair) : 7542 + 12 × (vitesse de pointe − 40) × 100 / 60 − 16 × `[joueur+0x5C]`. La stat de vitesse de pointe est l'octet `0x3BCF5AE` (corrélation de 1,00 avec `+0x58` sur 22 joueurs). Un point de stat ne vaut que 20 unités : 68 → 8094, 97 → 8682.
- Le code ajouté multiplie la part de la stat autour d'un joueur à 75. Comme la valeur est recalculée pendant le match, F6 s'applique tout de suite.

### Virages francs en sprint

Mesuré avec `pes6turnlog.ps1` (une ligne à chaque changement des champs du porteur du ballon) et des points d'arrêt d'exécution :

- En course balle au pied, une foulée dure environ 26 images (0,46 s, animations 65/66), avec une touche vers l'image 7 ; la nouvelle direction du stick n'est prise qu'à la foulée suivante. En sprint, une foulée dure environ 0,67 s (animations 29/30 = `0x1D`/`0x1E`). Un virage de plus de 112° passe par une animation de virage (552, environ 0,65 s).
- La foulée de sprint démarre dans `0x4E2830` (appelée depuis `0x5B7954` quand le bouton de sprint est tenu, `0x4DCBB0` sinon). Elle compare la direction du stick (`0x4B3010`) à l'orientation `+0x26`, en 256e de tour : en dessous de 8 rien ne se passe, à partir de `0x50` (112°) c'est l'animation de virage. Sinon :
  - la **direction du joueur** tourne d'un pas fixe : animation `0x1E` avec 15° (`push 0Fh` en `0x4E299D`) du côté du bon pied, `0x1D` avec 25° (`push 19h` en `0x4E2A51`) de l'autre, via `0x4F3820`, qui passe dans le code protégé ; les deux branches se rejoignent en `0x4E2A7E` avec la nouvelle direction dans `di`, écrite dans `+0x2C` en `0x4E2FA7` ;
  - la **direction du ballon** est calculée en `0x4E2AFF` : `0x4970F0` (tourner une direction vers une autre d'un angle maximum) depuis l'orientation arrondie à 22,5° (`(+0x26 + 0x800) & 0xF000`) vers le stick, d'au plus `0x1000` (`push 1000h` en `0x4E2AE6`). Relevé à la touche (appel du relais en `0x5AFB3B`, direction prise dans `[joueur+0x30]`) : `0xA000`, `0xB000`, `0xC000`… exactement un cran de 22,5° par touche, quoi que demande le stick. **C'est ce cran qui fait tourner « par paliers ».**
- Le joueur court ensuite après le ballon : `0x5AFD45` oriente `+0x2C` à chaque image vers la position prévue du ballon.
- **Virages plus francs en sprint** remplace le `push 1000h` par le virage maximum du porteur : 22,5° jusqu'à 65 en précision de dribble (celui du jeu), le maximum choisi (90° par défaut) à partir de 95, proportionnellement entre les deux. Une touche qui tourne le ballon de plus de 22,5° par rapport à sa direction actuelle (`[ballon+0x58]`) perd aussi jusqu'à 40 % de sa vitesse (atteint à 90°), à l'appel du relais (`0x5AFB32`), pour que le ballon reste plus près pendant que le joueur tourne. Testé en mémoire, puis validé en jeu.
- Essayé puis abandonné : relever le `push 2Dh` (45°) d'autres foulées (`0x4E1AAF`, `0x4E2231`, `0x4E2BF1`, `0x4E2D70` : elles ne servent jamais en dribble) ; relever les pas de 15° et 25° (sans effet : le code protégé les ignore) ; forcer la direction du joueur sur celle du stick (`0x4E2A7E` : le corps tourne, pas le ballon) ou sur celle de la touche (`0x4E2B04` : l'animation saute, et avec le changement du ballon le joueur ne tourne plus).

### Zone morte du stick

Mesuré avec `pes6turnlog.ps1` en dribblant vers la droite et en bougeant légèrement le stick vers le haut et le bas, puis avec des points d'arrêt en écriture :

- L'angle du stick (`0x1016680` + manette × 4) ne prend jamais de valeur à moins d'environ 16° de 0°, 90°, 180° ou 270° : il reste pile sur l'axe, puis saute à 16-17°. Le joueur continue donc sur une ligne, quoi que fasse le code des directions libres avec l'angle.
- L'angle est écrit en `0x4B23E7` à partir de 4 octets d'axe par manette (`0x3BCEE51`…, un octet par demi-axe), par une arctangente ; là, les deux axes sous `0x19` donnent la zone morte centrale.
- Ces octets viennent du lecteur de manette `0x8B7D63`… : chaque axe brut (0-255, centre 128) est mis à zéro quand il est à `0x25` (37) ou moins du centre (`cmp eax,25h` en `0x8B7D83`, `0x8B7DAA`, `0x8B7DD1`, `0x8B7DFC`), sans remise à l'échelle. C'est une zone morte de 29 % par axe, d'où les 16° (arctan(37/127)).
- **Zone morte du stick analogique réduite** abaisse les 4 limites (6 % = 8 par défaut). Validé en jeu : un léger mouvement du stick oriente le joueur. Elle vaut pour les deux sticks (stick droit : passes manuelles).
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
| Stats | `0x3BCF5A8` + (équipe × 32 + poste) × `0x348`, 7 bits par stat : `+0x0` bit 0 pied fort, `+0x6` vitesse de pointe, `+0x8` (lue pour l'accélération), `+0x18` précision de dribble |
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

| Script | Rôle |
|---|---|
| `DumpAt.java`, `DumpRefs.java`, `ListRefs.java`, `Disasm.java`, `FindDevCalls.java` | scripts Ghidra (mode headless) : décompiler une fonction, lister les références à une adresse, désassembler une plage, trouver les appels d'une méthode du périphérique Direct3D 8 |
| `pes6code.ps1`, `pes6dump.ps1` | lire ou modifier des octets dans le jeu en cours d'exécution (mémoire uniquement) |
| `pes6watch.ps1` | point d'arrêt matériel en écriture ou en lecture sur une adresse : quelles instructions la touchent |
| `pes6hits.ps1` | points d'arrêt matériels en exécution : combien de fois du code s'exécute, avec quels appelants et arguments |
| `pes6viewport.ps1` | relève les appels `SetViewport` (rectangle et appelant), ce qui a mené au découpage des listes |
| `pes6stick.ps1` | relevé mémoire guidé par bips (stick au repos, à droite, en haut), ce qui a mené à l'état de la manette |
| `pes6heading.ps1` | mesure le cap réel des joueurs face à l'angle du stick, avec et sans les directions libres |
| `pes6turnlog.ps1` | relevé du porteur du ballon guidé par des bips (état, animation, image, orientations, vitesse, direction du ballon) : une ligne CSV à chaque changement, qui a montré les crans de virage en sprint |
| `pes6window.ps1`, `capture-window.ps1` | géométrie et capture de la fenêtre du jeu ou de l'application |
| `make-signatures.ps1` | construit la signature d'un emplacement : les octets qui l'entourent, adresses absolues et cibles d'appel masquées, allongés jusqu'à n'apparaître qu'une fois dans le code |
| `StringRefs.java` | script Ghidra : décompile les fonctions qui utilisent une chaîne (par exemple les noms de versions de Kitserver dans `kload.dll`) |
| `set-resx.ps1` | ajoute, modifie ou supprime des textes dans les ressources anglaises et françaises |

Les options de gameplay ont surtout été trouvées avec des variantes de `pes6watch.ps1` et `pes6hits.ps1` (plusieurs adresses à la fois, registres et pile relevés à chaque passage), et avec des relevés mémoire des joueurs, du ballon et des stats pendant le jeu, ensuite comparés.

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
