# glb2fivem by Edmondio

Créé par **Edmondio** · [github.com/Edmondio/glb2fivem](https://github.com/Edmondio/glb2fivem)

Convertit un modèle 3D **.glb / .gltf** en **prop FiveM prêt à streamer** :
`.ydr` (modèle + collision) + `.ytd` (textures) + `.ytyp` (archétype) + `fxmanifest.lua`.

Tout est local et open source : CodeWalker.Core compile les formats RAGE, SharpGLTF lit le GLB et
meshoptimizer réduit les polygones. L'encodeur DXT est écrit en C# dans le projet.

## Installation (Windows)

Prérequis : [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) et Git.

```powershell
powershell -ExecutionPolicy Bypass -File tools\setup-deps.ps1   # récupère les sources des dépendances (versions figées)
dotnet build src\Glb2FiveM.Cli -c Release
```

`native\win-x64\meshoptimizer.dll` est déjà compilée (sans dépendance). Pour la recompiler, lance
`tools/build-meshopt.sh` sous Linux ou WSL.

## Créer les .exe soi-même

Double-clique sur **`build-exe.bat`**. Il vérifie le SDK .NET 8, récupère les sources des dépendances si besoin, puis compile :

- `dist\glb2fivem-gui.exe` : l'interface (s'ouvre dans sa propre fenêtre via Edge ou Chrome en mode application)
- `dist\glb2fivem.exe` : la ligne de commande

Les deux sont **autonomes** (runtime .NET, DLL meshoptimizer et interface embarqués, ~36 Mo chacun) : ils se copient sur n'importe quel PC Windows 64 bits sans rien installer. La première compilation télécharge le runtime .NET Windows depuis nuget.org (internet requis), ensuite il est en cache.

Commande équivalente :

```powershell
dotnet publish src\Glb2FiveM.Gui -c Release -r win-x64 -p:PublishSingleFile=true -p:EnableSingleFileAnalyzer=false -p:DebugType=none -o dist --source https://api.nuget.org/v3/index.json
```

Options de `glb2fivem-gui.exe` : `--browser` (onglet du navigateur au lieu d'une fenêtre), `--no-browser`, `--port <n>`.
Le dernier dossier de sortie est retenu dans `%LOCALAPPDATA%\glb2fivem\reglages.json`.

## Interface

Double-clique sur **`lancer-interface.bat`** (ou `dotnet run --project src\Glb2FiveM.Gui -c Release`).
Une page s'ouvre dans ton navigateur :

- interface en **français ou anglais** (boutons FR / EN en haut ; par défaut la langue de Windows), journal de conversion compris
- glisser-déposer du `.glb`, aperçu 3D avec un mannequin de 1,80 m et une grille au sol (1 m / 0,5 m)
- hauteur finale (curseur ou valeur exacte), rotation, point d'origine ; les dimensions affichées sont celles du prop final
- détection des modèles exportés en centimètres
- triangles max (10k / 50k / 150k ou valeur libre), taille des textures, LOD, collision
- vue Original ou LOD calculés (High → VeryLow), fil de fer, collision réelle en surimpression, tableau triangles / vertices
- onglet **Textures** : couleur de chaque matériau, luminosité / contraste / saturation / teinte, remplacement d'image, export PNG ; les retouches sont visibles en direct et intégrées au YTD
- **éditeur d'image intégré** (bouton « ✏ Éditer » sur chaque texture, basé sur miniPaint) : pinceau, gomme, tampon, texte, calques, sélection, recadrage, filtres… « Appliquer à la texture » remplace l'image du modèle (aperçu en direct, puis YTD)
- par matériau : **émissif** (toujours allumé, seulement la nuit, ou désactivé) avec son intensité, et **occlusion** (AO) cuite dans la couleur avec sa force
- onglet **Lumières** : clic sur le modèle pour poser une lumière (ponctuelle, spot ou **tube néon** avec sa longueur), avec couleur, **taille** (de 1 cm pour une LED à 3 m), intensité, portée, angle, allumage (toujours ou la nuit), effet (bougie, feu, néon, alarme, stroboscope…) et ombres ; aperçu animé en mode nuit, lumières intégrées au `.ydr`
- placement précis des lumières : fantôme sous la souris, poignée 3D (G : déplacer, R : orienter un spot), pas magnétique 1 / 5 / 10 cm, coordonnées X / Y / Z au centimètre
- **aperçu CodeWalker** : un `.ydr` avec textures et lumières intégrées est créé dans `<nom>_apercu_codewalker` ; dans CodeWalker, RPF Explorer → File → Open Folder, double-clic sur le `.ydr`, puis Options → Lighting (Deferred shading, HD lights, Skydome, Time of day 22:00)
- onglet **Nettoyer** : supprimer ce qui entoure le modèle (dôme de décor, débris…). Trois outils : **Pièce** (un objet du GLB), **Îlot** (un morceau détaché), **Zone** (rectangle, y compris ce qui est derrière). Supprimer, garder uniquement la sélection, annuler (Ctrl+Z), puis « Appliquer et recharger » : le modèle propre est réexporté en GLB en gardant l'échelle choisie
- « Convertir » (ou Ctrl+Entrée), puis récap : LOD, textures, fichiers, `ensure`, code de spawn et bouton pour ouvrir le dossier

La page vient d'un mini-serveur local (127.0.0.1, protégé par un jeton). Ferme la fenêtre console pour quitter.

## Ligne de commande

```powershell
dotnet run --project src\Glb2FiveM.Cli -c Release -- mon_modele.glb -o C:\fivem\resources --height 1.1 --lods
```

On obtient `C:\fivem\resources\mon_modele\` (stream/ + fxmanifest.lua). Il suffit d'ajouter `ensure mon_modele` dans le server.cfg.

| Option | Rôle | Défaut |
|---|---|---|
| `-o, --out` | Dossier de sortie | `./output` |
| `-n, --name` | Nom du prop (minuscules, `a-z0-9_`) | nom du fichier |
| `--max-tris` | Plafond de triangles du LOD High, décimation au-delà | 150000 |
| `--height` | Hauteur finale en mètres (prioritaire sur `--scale`) | taille d'origine |
| `--scale` | Facteur d'échelle | 1 |
| `--rotate` | Rotation autour de l'axe vertical (degrés) | 0 |
| `--pivot` | `base` (posé au sol), `center`, `origin` | base |
| `--max-texture` | Taille max des textures (puissance de 2) | 2048 |
| `--lods` | Génère Medium / Low / VeryLow (50 / 25 / 10 %) | non |
| `--collision` | `box` (boîte), `mesh` (forme du modèle, ~2000 triangles) ou `none` | box |
| `--collision-tris` | Triangles max de la collision `mesh` | 2000 |
| `--lod-dist` | Distance d'affichage du prop (m) | auto (100 à 500) |
| `--flags` | Flags d'archétype YTYP | 32 (Static) |
| `--no-flip-normal` | Ne pas inverser le canal vert des normal maps | inversé |
| `--keep-vertex-colors` | Garder les couleurs de vertex du GLB | blanc |
| `--lights` | Fichier JSON de lumières (liste de `LightDef`, coordonnées GTA du prop) | aucune |
| `--emissive` | Émissif : `always`, `night` ou `off` | always |
| `--emissive-intensity` | Multiplicateur de lumière de l'émissif (0 à 16) | 5 |
| `--no-ao` | Ne pas cuire l'occlusion (AO) dans la couleur | cuite |
| `--codewalker` | Crée aussi l'aperçu CodeWalker (textures intégrées) | non |
| `--lang` | Langue des messages : `fr` ou `en` | langue de Windows |
| `--keep-xml` | Garder les XML CodeWalker et les DDS (ouvrables dans CodeWalker) | non |

Repère d'échelle : un ped GTA mesure environ 1,80 m.

## Ce que fait la conversion

1. **Lecture du GLB** : tous les nœuds avec leurs transformations, une partie par matériau, `KHR_texture_transform` appliqué.
2. **Axes** : glTF (Y en haut) vers GTA (Z en haut). Puis l'échelle et le pivot (par défaut au centre de la base).
3. **Polygones** : fusion des vertices identiques. Au-delà de `--max-tris`, décimation meshoptimizer qui respecte les coutures d'UV et les normales. Si un maillage très découpé bloque, une seconde passe plus agressive prend le relais. Les géométries sont découpées à 65 535 vertices (indices 16 bits de GTA).
4. **Matériaux** :
   - normal map → `normal_spec.sps` (la map spéculaire est générée depuis metallic/roughness)
   - metallic/roughness seul → `spec.sps`
   - sinon → `default.sps`
   - alpha BLEND → variante `_alpha` ; alpha MASK → variante `_cutout`
   - **émissif** : seuls les triangles qui brillent sont dupliqués en surimpression avec le shader de Rockstar `decal_emissive_only` (toujours) ou `decal_emissivenight_only` (de 20 h à 6 h). La texture `_e` porte la couleur de la lueur et son masque.
   - **occlusion** : la map AO du GLB est multipliée dans la couleur (GTA n'a pas de slot AO sur les props).
5. **Textures** : passage en puissance de 2, mipmaps, DXT1 (opaque, normal) ou DXT5 (alpha, spéculaire).
6. **Collision** : intégrée au YDR, sous forme de boîte englobante ou de maillage simplifié (BVH). L'archétype YTYP la référence via `physicsDictionary`.
7. **Vérification** : les binaires produits sont relus avec CodeWalker avant d'être écrits.

## Outil de vérification

`tests/Glb2FiveM.Inspect` relit une ressource générée :

```powershell
dotnet run --project tests\Glb2FiveM.Inspect -- <dossier stream> <dossier sortie>             # YDR/YTYP -> XML, YTD -> PNG
dotnet run --project tests\Glb2FiveM.Inspect -- render <prop.ydr> <prop.ytd> apercu.png [lod]  # rendu à côté d'un mannequin de 1,80 m
```

## Limites actuelles

- Props statiques uniquement : pas de vêtements, de véhicules ni de modèles riggés (exportés dans leur pose de repos).
- L'émissif est une surimpression : il éclaire la surface (glow) mais n'éclaire pas l'entourage. Pour éclairer autour, ajoute une lumière.
- Textures WebP / KTX2 non décodées (couleur unie à la place, avec un avertissement).
- La collision « forme du modèle » (maillage BVH) n'est active que si le prop est figé (`FreezeEntityPosition(obj, true)`).
- Rendu en jeu à valider : sens du vert des normal maps, réglages spéculaires, alpha.

## Licences des dépendances

- SharpGLTF : MIT. StbImageSharp : domaine public. meshoptimizer : MIT. SharpDX : MIT. three.js : MIT.
- **miniPaint** (viliusle) : MIT. Il embarque AlertifyJS, sous GPL-3 : si tu redistribues le .exe publiquement, fournis aussi les sources (ce dépôt) pour rester en règle.
- **CodeWalker** (dexyfex) : le dépôt n'a pas de licence explicite. Pas de souci pour un usage perso ou interne. Avant de distribuer publiquement un .exe qui l'embarque, il vaut mieux demander à l'auteur.
