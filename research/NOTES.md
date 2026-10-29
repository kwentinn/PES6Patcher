# PES6 — affichage 16:9 et directions libres

Modifications de Pro Evolution Soccer 6 (PC) installé dans
`C:\Program Files (x86)\KONAMI\Pro Evolution Soccer 6`.

Version installée : **v6** de `PES6.exe`, profil **32:9**. Les deux profils, 16:9 et 32:9, sont validés en jeu.

- Menus, listes, HUD et curseur du joueur affichés sans étirement en 16:9.
- Gameplay identique à l'original.
- Course du joueur à l'angle précis du stick analogique, au lieu de 8 directions seulement.

---

## 1. Réglages modifiés (hors exe)

| Fichier | Réglage | Valeur |
|---|---|---|
| `kitserver\kload.cfg` | `dx.fullscreen.width/height`, `internal.resolution.width/height` | 2560 × 1440 |
| `kitserver\speeder.cfg` | `count.factor` | 1.0 (vitesse d'origine) |
| `kitserver\lodmixer.dat` | format d'image manuel (octets 41–44) | 1.77778 (16:9) |
| `dgVoodoo.conf` | `ScalingMode` | `stretched_ar` (image centrée, bandes noires) |

Une copie d'origine de chaque fichier existe à côté avec l'extension `.bak` :
`kload.cfg.bak`, `speeder.cfg.bak`, `lodmixer.dat.bak` et `dgVoodoo.conf.bak`.

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

## 3. Fichiers de travail (dossier de la session)

| Chemin | Contenu |
|---|---|
| `scripts\pes6patch.ps1` | construit un exe patché à partir de l'original, en vérifiant chaque octet avant de le modifier |
| `scripts\pes6clipcode.ps1`, `pes6cursorcode.ps1`, `pes6dircode.ps1` | génèrent le code ajouté (découpage, curseur, directions) |
| `scripts\pes6*.ps1` (autres) | outils de diagnostic en mémoire : lecture/écriture, points d'arrêt matériels, relevés guidés par bips |
| `scripts\*.java` | scripts Ghidra (décompilation, références, désassemblage) |
| `work\PES6.exe` | copie de l'exe d'origine analysée |
| `work\PES6_ws.exe`, `PES6_ws_safe.exe`, `PES6_ws_v3.exe` … `PES6_ws_v6.exe` | versions successives (v5 = sans directions libres, v6 = installée) |
| `work\proj\` | projet Ghidra de `PES6.exe` |
| `tools\` | JDK 21 et 25, copie de Ghidra 12.2 |

Ghidra 12.2 est installé hors de ce dossier, dans `C:\Users\kwent\ghidra12`. Ce chemin court est nécessaire, car son décompilateur ne se lance pas depuis un chemin de plus de 260 caractères.

---

## 4. Reconstruire l'exe

16:9 avec directions libres (= v6) :

```powershell
.\scripts\pes6patch.ps1 -In work\PES6.exe -Out work\PES6_ws.exe -Half 398 -WidthFn -FreeDir
```

Sans directions libres (= v5) : retirer `-FreeDir`.

**32:9 (Odyssey G9 en plein écran)** : utiliser `-Half 796`, régler la résolution `5120 × 1440` dans `kload.cfg`, et passer le format de lodmixer à `3.55556`.

Pour installer, fermer le jeu puis copier l'exe construit à la place de `PES6.exe`. Les droits administrateur sont nécessaires.

---

## 5. Basculer entre 16:9 et 32:9

Deux profils complets sont prêts dans `work\profiles\`. Chacun contient `PES6.exe`, `kload.cfg`, `lodmixer.dat` et `settings.dat` :

| Profil | Exe | Rendu (kload) | Fenêtre | Résolution du jeu (settings.dat) | Format 3D (lodmixer) |
|---|---|---|---|---|---|
| `16x9` | v6, `-Half 398` | 2560 × 1440 | normale | 1920 × 1080 | 1.77778 |
| `32x9` (installé, validé sur le G9) | v6, `-Half 796` | 5120 × 1440 | sans bordure, 5120 × 1440 en (0, 0) | 5120 × 1440 | 3.55556 |

Points propres au 32:9 :

- **Fenêtre sans bordure.** `kload.cfg` impose la fenêtre avec `dx.window.x/y/width/height` et `dx.window.style = 0x96000000`. Elle couvre exactement l'écran. Il faut rester en mode fenêtré dans le jeu.
- **Résolution du jeu.** `settings.exe` ne propose pas plus de 1920 × 1080. Elle est donc écrite directement dans `Documents\KONAMI\Pro Evolution Soccer 6\settings.dat` :
  - largeur aux octets 4-5 ;
  - hauteur aux octets 6-7 ;
  - aux octets 2-3, une somme de contrôle : la somme sur 16 bits des octets à partir du 4ᵉ.

  L'original est sauvegardé dans `settings.dat.bak`. Ne pas rouvrir `settings.exe` en 32:9 : il remettrait 1920 × 1080.
- **Mise à l'échelle Windows.** Windows est à 125 % sur le G9. Le jeu gère correctement cette mise à l'échelle, ce n'était pas la cause du décalage.

Jeu fermé, lancer :

```powershell
.\scripts\pes6format.ps1 -Format 32x9
```

Pour revenir au 16:9, lancer la même commande avec `-Format 16x9`.

Le script copie les quatre fichiers, puis vérifie leurs empreintes. Windows demande les droits administrateur pour `PES6.exe`. PES6 et `settings.exe` doivent être fermés.

En 32:9, les menus n'occupent qu'environ un tiers de la largeur, au centre.

---

## 6. Revenir en arrière

- **Original** : remplacer `PES6.exe` par `PES6.exe.orig`, puis restaurer les fichiers `.bak` des réglages.
- **v5** (affichage corrigé, sans directions libres) : copier `work\PES6_ws_v5.exe` à la place de `PES6.exe`.

---

## 7. Limites connues

- Les décors des menus ne font que 512 de large : bandes noires sur les côtés.
- Dans le menu principal, une seconde copie de l'image du joueur apparaît dans la bande droite.
- Les directions libres ne corrigent que ±22,5° autour des 8 directions, et l'effet est subtil. PES6 fait virer le joueur par paliers : un vrai déplacement à 360° demanderait de refondre son système de course.
- Le patch est calculé pour cette version précise de `PES6.exe`. Le script refuse de patcher un exe différent.
