# PES6 — notes de recherche

Comment les correctifs de PES6 Patcher ont été trouvés et ce qu'ils modifient. La « v6 » est la version de `PES6.exe` validée en jeu, en 16:9 et en 32:9, que l'application reproduit octet pour octet.

- Menus, listes, HUD et curseur du joueur affichés sans étirement.
- Gameplay identique à l'original.
- Course du joueur à l'angle précis du stick analogique, au lieu de 8 directions seulement.

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

## 3. Outils de recherche

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
| `pes6window.ps1`, `capture-window.ps1` | géométrie et capture de la fenêtre du jeu ou de l'application |
| `make-signatures.ps1` | construit la signature d'un emplacement : les octets qui l'entourent, adresses absolues et cibles d'appel masquées, allongés jusqu'à n'apparaître qu'une fois dans le code |
| `StringRefs.java` | script Ghidra : décompile les fonctions qui utilisent une chaîne (par exemple les noms de versions de Kitserver dans `kload.dll`) |
| `set-resx.ps1` | ajoute, modifie ou supprime des textes dans les ressources anglaises et françaises |

### Plusieurs versions de l'exe

Kitserver distingue trois versions du jeu : « PES6 PC » (1.00), « PES6 PC 1.10 » et « WE-PES 2007 PC ». Les patchs communautaires fournissent aussi leurs propres exe. Plutôt qu'une table d'adresses par version, qui demanderait de posséder chaque exe, l'application retrouve les emplacements par signature (`src/PES6Patcher.Core/Patching/ExeLocator.cs`) :

- les signatures sont celles de `make-signatures.ps1`, construites vers l'avant à partir de chaque emplacement ;
- les adresses utilisées par le code ajouté (constantes 256,0 et 1/512, fonction de largeur de l'écran, variable des bits de direction, table du stick…) sont lues dans le code trouvé, puis vérifiées ;
- les deux appels qui transmettent la largeur à la 3D sont reconnus par le contenu de la fonction appelée, car un troisième appel identique alimente le découpage 2D ;
- les zones libres sont les plages de `CC` d'au moins 80 octets du code, et les constantes vont dans la dernière page de `.rdata`, chargée par Windows même au-delà des données du fichier.

Sur l'exe de référence, ce moteur produit octet pour octet la v6 validée en jeu (tests `Build_matches_the_exe_validated_in_game`).

Le décompilateur de Ghidra ne se lance pas depuis un chemin de plus de 260 caractères : installer Ghidra dans un dossier court.

---

## 4. Points techniques de la configuration écran large

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

## 5. Installer et revenir en arrière

L'application PES6 Patcher installe ces réglages pour n'importe quelle résolution. Son bouton **Restaurer l'original** remet tous les fichiers tels qu'ils étaient avant sa première utilisation.

---

## 6. Limites connues

- Les décors des menus ne font que 512 de large : bandes noires sur les côtés.
- Dans le menu principal, une seconde copie de l'image du joueur apparaît dans la bande droite.
- Les directions libres ne corrigent que ±22,5° autour des 8 directions, et l'effet est subtil. PES6 fait virer le joueur par paliers : un vrai déplacement à 360° demanderait de refondre son système de course.
- Le patch est calculé pour cette version précise de `PES6.exe`. Le script refuse de patcher un exe différent.
