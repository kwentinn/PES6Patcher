# PES6 Patcher

Patch pour **Pro Evolution Soccer 6 (PC)** : affichage écran large sans étirement (16:9, 21:9, 32:9…) et déplacement plus libre au stick analogique.

![PES6 Patcher](docs/app.png)

## Ce que fait le patch

**Affichage écran large**
- Les menus, les listes, le HUD (score, chrono, radar, noms) et le curseur du joueur ne sont plus étirés. Ils gardent leurs proportions d'origine, au centre de l'écran.
- L'image 3D est adaptée au format de l'écran avec LOD Mixer.
- Au-delà de 1920 × 1080 (écrans 1440p, ultra-larges), le jeu s'ouvre dans une fenêtre sans bordure qui couvre tout l'écran.

**Gameplay**
- **Directions libres au stick analogique** : le joueur suit l'angle précis du stick, à ±22,5° autour des 8 directions d'origine, au lieu de ces seules 8 directions. La croix directionnelle, le clavier et l'IA ne changent pas.
- **Vitesse du jeu** réglable (module Speeder de Kitserver).

Le reste du gameplay est inchangé.

## Prérequis

- **PES6 PC avec la version de `PES6.exe` supportée.** Les adresses du patch ont été trouvées dans une seule version de l'exe, identifiée par son empreinte SHA-256 :
  `3FE13850C933C85385F3FB5C45977D2DAA860D1FC03E95E97BE9F4FC24D81382`.
  L'application refuse tout autre exe et ne modifie alors rien.
- **[Kitserver 6](https://www.evoweb.uk/)**, avec les modules LOD Mixer et Speeder. Il sert à régler la résolution et le format 3D.
- **Windows 10/11** et le **.NET 10 Desktop Runtime**.

## Utilisation

1. Fermer PES6 et `settings.exe`.
2. Lancer `PES6Patcher.exe`. Il demande les droits administrateur, car le jeu est installé dans `Program Files`.
3. Vérifier le dossier du jeu (détecté automatiquement) et la résolution (celle de l'écran par défaut).
4. Choisir les options, puis cliquer sur **Appliquer**.

**Restaurer l'original** remet tous les fichiers tels qu'ils étaient avant la première application.

Remarques :
- Après application, ne pas rouvrir `settings.exe` : il ne connaît pas les résolutions au-delà de 1920 × 1080 et remettrait l'ancienne valeur.
- Rester en mode fenêtré dans le jeu. La fenêtre sans bordure couvre déjà tout l'écran.

## Fichiers modifiés

| Fichier | Modification | Sauvegarde |
|---|---|---|
| `PES6.exe` | patch (voir ci-dessous) | `PES6.exe.orig` |
| `kitserver\kload.cfg` | résolution de rendu, fenêtre sans bordure | `kload.cfg.orig` |
| `kitserver\lodmixer.dat` | format d'image 3D | `lodmixer.dat.orig` |
| `kitserver\speeder.cfg` | vitesse du jeu | `speeder.cfg.orig` |
| `Documents\KONAMI\Pro Evolution Soccer 6\settings.dat` | résolution du jeu, somme de contrôle recalculée | `settings.dat.orig` |

## Fonctionnement

Le jeu dessine ses menus dans un repère hérité de la PS2, de **512 × 448** unités, puis l'étire sur toute la largeur de l'écran. Le patch élargit ce repère à **448 × format** unités, par exemple 796 × 448 en 16:9. Le rendu 3D et le gameplay continuent d'utiliser les valeurs d'origine.

| Élément | Modification |
|---|---|
| Conversion repère 2D → pixels | 13 instructions graphiques lisent de nouvelles constantes (demi-largeur, 1/largeur) au lieu de 256 et 1/512. Les instructions de gameplay qui lisent ces mêmes constantes ne sont pas touchées. |
| Largeur du repère 2D | La fonction qui renvoie 512 renvoie la nouvelle largeur. Les deux appels qui alimentent la projection 3D gardent 512, sinon les joueurs « glissent ». |
| Découpage des listes | Code ajouté avant la conversion du rectangle de découpage en pixels. |
| Curseur du joueur | Code ajouté qui ajuste l'échelle de la projection 3D → repère 2D. |
| Directions libres | Code ajouté autour de la conversion bits de direction → cap : ajoute l'écart entre l'angle précis du stick et le centre de son secteur de 45°. Le seuil de virage de la course passe de 45° à environ 17°. |

Le code ajouté est placé dans des zones de remplissage inutilisées de l'exe. Avant toute écriture, chaque emplacement est vérifié par rapport aux octets d'origine.

Le détail des adresses et de la méthode, avec Ghidra, des points d'arrêt matériels et des relevés mémoire, est dans [`research/NOTES.md`](research/NOTES.md). Les scripts utilisés pendant la recherche sont dans [`research/scripts`](research/scripts).

## Limites

- Une seule version de `PES6.exe` est supportée.
- Les décors des menus font 512 unités de large : il reste des bandes noires sur les côtés.
- Les directions libres corrigent ±22,5° autour des 8 directions. Un vrai 360° demanderait de refondre le système de course du jeu.

## Compiler

```powershell
dotnet build
dotnet test
dotnet publish src/PES6Patcher -c Release -o publish
```

Les tests comparent l'exe produit avec des exe validés en jeu. Ces fichiers viennent du jeu et ne sont donc pas dans le dépôt : sans eux, ces tests passent sans rien vérifier.

## Avertissement

Projet non officiel, sans lien avec Konami. Aucun fichier du jeu n'est inclus : l'application modifie uniquement l'installation présente sur la machine, après en avoir fait une sauvegarde. Utilisation à vos risques.

## Licence

[MIT](LICENSE)

