# GLB2FiveM — 3D Prop Studio

**Convert. Customize. Optimize. Stream.**

[![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue)](https://github.com/Edmondio/glb2fivem) [![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0) [![FiveM](https://img.shields.io/badge/FiveM-GTA%20V-orange)](https://fivem.net/) [![Language](https://img.shields.io/badge/languages-EN%20%7C%20FR-blue)](#français)

Created by **Edmondio** · [GitHub](https://github.com/Edmondio/glb2fivem)

[English](#english) | [Français](#français)

---

## English

**GLB2FiveM — 3D Prop Studio** is a local Windows tool for converting `.glb` and `.gltf` 3D models into ready-to-stream FiveM props, with a graphical editor and a command-line interface.

| Output | Purpose |
|---|---|
| `.ydr` | Model, collision and lights |
| `.ytd` | Texture dictionary |
| `.ytyp` | Archetype definition |
| `fxmanifest.lua` | FiveM resource manifest |

CodeWalker.Core compiles RAGE formats, SharpGLTF reads glTF models, and meshoptimizer reduces polygon counts. The DXT encoder is implemented in C# within the project. Processing takes place locally.

### Installation (Windows)

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and Git.

```powershell
powershell -ExecutionPolicy Bypass -File tools\setup-deps.ps1 # fetch pinned dependency sources
dotnet build src\Glb2FiveM.Cli -c Release
```

`native\win-x64\meshoptimizer.dll` is already compiled and has no external dependencies. To rebuild it, run `tools/build-meshopt.sh` on Linux or WSL.

### Build standalone executables

Double-click `build-exe.bat`. It checks the .NET 8 SDK, fetches dependency sources if needed and builds:

- `dist\glb2fivem-gui.exe`: graphical interface, opened in its own Edge or Chrome app window.
- `dist\glb2fivem.exe`: command-line interface.

Both are self-contained (embedded .NET runtime, meshoptimizer DLL and interface as applicable, approximately 36 MB each) and can be copied to a 64-bit Windows PC without installing .NET. The first build downloads the Windows runtime from NuGet; subsequent builds can use the cache.

Equivalent GUI build command:

```powershell
dotnet publish src\Glb2FiveM.Gui -c Release -r win-x64 -p:PublishSingleFile=true -p:EnableSingleFileAnalyzer=false -p:DebugType=none -o dist --source https://api.nuget.org/v3/index.json
```

GUI options: `--browser` (browser tab instead of app window), `--no-browser`, `--port <n>`.
The last output directory is saved in `%LOCALAPPDATA%\glb2fivem\reglages.json`.

### Graphical interface

Double-click `lancer-interface.bat` or run:

```powershell
dotnet run --project src\Glb2FiveM.Gui -c Release
```

- **Bilingual interface:** French and English, including conversion logs; defaults to the Windows language.
- **3D preview:** drag and drop a GLB, with a 1.80 m reference mannequin and a 1 m / 0.5 m floor grid.
- **Transformations:** exact height or slider, rotation, pivot and final dimensions; detection of models exported in centimeters.
- **Optimization:** triangle limits (10k / 50k / 150k or custom), texture size, LOD and collision settings.
- **Inspection:** original and generated LOD views (High to VeryLow), wireframe, actual collision overlay and triangle/vertex statistics.
- **Textures:** per-material color, brightness, contrast, saturation, hue, image replacement and PNG export. Changes appear live and are included in the YTD.
- **Integrated image editor:** miniPaint provides brushes, eraser, clone stamp, text, layers, selections, cropping and filters. Apply changes directly to the model texture.
- **Materials:** per-material emissive mode (always, night only or off), emissive intensity and ambient occlusion baking strength.
- **Lighting:** place point lights, spotlights and neon tubes directly on the model; configure color, size (1 cm to 3 m), tube length, intensity, range, angle, activation mode, shadows and effects (candle, fire, neon, alarm, strobe). Animated night preview; lights are embedded in the YDR.
- **Precise light placement:** cursor preview, 3D gizmo (G to move, R to rotate a spotlight), 1/5/10 cm snapping and XYZ coordinates accurate to the centimeter.
- **CodeWalker preview:** generates a YDR with embedded textures and lights in `<name>_apercu_codewalker`. In CodeWalker, use RPF Explorer → File → Open Folder, double-click the YDR, then Options → Lighting (Deferred shading, HD lights, Skydome, Time of day 22:00).
- **Model cleanup:** remove surrounding geometry such as scenery domes or debris by object, disconnected island or rectangular region (including geometry behind the visible surface). Delete, keep only selection, undo with Ctrl+Z, then apply and reload to re-export a clean GLB while retaining the selected scale.
- **Export:** click Convert or press Ctrl+Enter for a summary of LODs, textures, files, the `ensure` instruction, spawn code and an output folder shortcut.

The interface is served by a token-protected local server at `127.0.0.1`. Close the console window to exit.

### Command-line interface

```powershell
dotnet run --project src\Glb2FiveM.Cli -c Release -- my_model.glb -o C:\fivem\resources --height 1.1 --lods
```

This creates `C:\fivem\resources\my_model\` with `stream/` and `fxmanifest.lua`. Add `ensure my_model` to `server.cfg`.

| Option | Purpose | Default |
|---|---|---|
| `-o, --out` | Output directory | `./output` |
| `-n, --name` | Prop name (lowercase, `a-z0-9_`) | File name |
| `--max-tris` | High LOD triangle limit; simplify above it | 150000 |
| `--height` | Final height in meters; overrides `--scale` | Original size |
| `--scale` | Scale factor | 1 |
| `--rotate` | Vertical-axis rotation in degrees | 0 |
| `--pivot` | `base`, `center`, `origin` | base |
| `--max-texture` | Maximum texture size (power of two) | 2048 |
| `--lods` | Generate Medium / Low / VeryLow (50 / 25 / 10%) | No |
| `--collision` | `box`, `mesh` (~2000 triangles), or `none` | box |
| `--collision-tris` | Maximum mesh collision triangles | 2000 |
| `--lod-dist` | Prop display distance in meters | Auto (100–500) |
| `--flags` | YTYP archetype flags | 32 (Static) |
| `--no-flip-normal` | Do not flip normal map green channel | Flipped |
| `--keep-vertex-colors` | Preserve GLB vertex colors | White |
| `--lights` | JSON list of `LightDef` in GTA prop coordinates | None |
| `--emissive` | `always`, `night`, or `off` | always |
| `--emissive-intensity` | Emissive brightness multiplier (0–16) | 5 |
| `--no-ao` | Disable ambient occlusion color baking | Baked |
| `--codewalker` | Generate CodeWalker preview with embedded textures | No |
| `--lang` | Message language: `fr` or `en` | Windows language |
| `--keep-xml` | Keep CodeWalker XML and DDS files | No |

Scale reference: a GTA ped is approximately 1.80 m tall.

### Conversion pipeline

1. **GLB parsing:** read all nodes and transforms, one part per material, applying `KHR_texture_transform`.
2. **Coordinate conversion:** glTF Y-up to GTA Z-up, followed by scaling and pivot placement (base center by default).
3. **Geometry:** merge identical vertices. Above `--max-tris`, meshoptimizer simplifies geometry while preserving UV seams and normals. A more aggressive second pass handles difficult meshes. Geometry is split at 65,535 vertices for GTA's 16-bit indices.
4. **Materials:**
   - Normal map → `normal_spec.sps` (specular map generated from metallic/roughness).
   - Metallic/roughness only → `spec.sps`.
   - Otherwise → `default.sps`.
   - Alpha BLEND → `_alpha`; alpha MASK → `_cutout`.
   - Emissive triangles are duplicated as overlays using Rockstar's `decal_emissive_only` (always) or `decal_emissivenight_only` (8 PM–6 AM). The `_e` texture stores glow color and mask.
   - Ambient occlusion is multiplied into the base color because GTA props have no dedicated AO slot.
5. **Textures:** power-of-two resizing, mipmaps, DXT1 (opaque, normal) or DXT5 (alpha, specular).
6. **Collision:** bounding box or simplified BVH mesh embedded in the YDR and referenced by the YTYP `physicsDictionary`.
7. **Validation:** generated binaries are read back through CodeWalker before being written.

### Inspection tool

`tests/Glb2FiveM.Inspect` reads generated resources:

```powershell
dotnet run --project tests\Glb2FiveM.Inspect -- <stream-folder> <output-folder> # YDR/YTYP to XML, YTD to PNG
dotnet run --project tests\Glb2FiveM.Inspect -- render <prop.ydr> <prop.ytd> preview.png [lod] # render with 1.80 m mannequin
```

### Current limitations

- Static props only: no clothing, vehicles or rigged models (rigged geometry is exported in its rest pose).
- Emissive overlays make surfaces glow but do not illuminate their surroundings; add a light for that.
- WebP / KTX2 textures are not decoded; a solid color is used with a warning.
- Mesh BVH collision is active only when the prop is frozen (`FreezeEntityPosition(obj, true)`).
- In-game rendering still requires validation, including normal map green channel orientation, specular settings and alpha.

### Third-party licenses

- SharpGLTF, meshoptimizer, SharpDX and three.js: MIT. StbImageSharp: public domain (as documented by the project).
- **miniPaint** (viliusle): MIT; includes AlertifyJS, under GPL-3 according to the original project documentation. Review applicable source distribution obligations before publishing executables.
- **CodeWalker** (dexyfex): no explicit license identified in the original project documentation. Obtain permission or verify redistribution rights before publicly distributing binaries or bundled source containing CodeWalker.

No overall project license is asserted here; dependency licensing and redistribution rights should be reviewed before release.

---

## Français

Convertit un modèle 3D **.glb / .gltf** en **prop FiveM prêt à streamer** :

`.ydr` (modèle + collision) + `.ytd` (textures) + `.ytyp` (archétype) + `fxmanifest.lua`.

Tout le traitement est effectué localement : CodeWalker.Core compile les formats RAGE, SharpGLTF lit le GLB et

meshoptimizer réduit les polygones. L'encodeur DXT est écrit en C# dans le projet.

### Installation (Windows)

Prérequis : [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) et Git.
```powershell

powershell -ExecutionPolicy Bypass -File tools\setup-deps.ps1   # récupère les sources des dépendances (versions figées)

dotnet build src\Glb2FiveM.Cli -c Release
```

`native\win-x64\meshoptimizer.dll` est déjà compilée (sans dépendance). Pour la recompiler, lance

`tools/build-meshopt.sh` sous Linux ou WSL.

### Créer les .exe soi-même

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

### Interface

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

### Ligne de commande
```powershell

dotnet run --project src\Glb2FiveM.Cli -c Release -- mon_modele.glb -o C:\fivem\resources --height 1.1 --lods
```

On obtient `C:\fivem\resources\mon_modele\\` (stream/ + fxmanifest.lua). Il suffit d'ajouter `ensure mon_modele` dans le server.cfg.
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

### Ce que fait la conversion

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

### Outil de vérification

`tests/Glb2FiveM.Inspect` relit une ressource générée :
```powershell

dotnet run --project tests\Glb2FiveM.Inspect -- <dossier stream> <dossier sortie>             # YDR/YTYP -> XML, YTD -> PNG

dotnet run --project tests\Glb2FiveM.Inspect -- render <prop.ydr> <prop.ytd> apercu.png [lod]  # rendu à côté d'un mannequin de 1,80 m
```

### Limites actuelles

- Props statiques uniquement : pas de vêtements, de véhicules ni de modèles riggés (exportés dans leur pose de repos).

- L'émissif est une surimpression : il éclaire la surface (glow) mais n'éclaire pas l'entourage. Pour éclairer autour, ajoute une lumière.

- Textures WebP / KTX2 non décodées (couleur unie à la place, avec un avertissement).

- La collision « forme du modèle » (maillage BVH) n'est active que si le prop est figé (`FreezeEntityPosition(obj, true)`).

- Rendu en jeu à valider : sens du vert des normal maps, réglages spéculaires, alpha.

### Licences des dépendances

- SharpGLTF : MIT. StbImageSharp : domaine public. meshoptimizer : MIT. SharpDX : MIT. three.js : MIT.

- **miniPaint** (viliusle) : MIT. Il embarque AlertifyJS, sous GPL-3 : vérifier les obligations de distribution des sources applicables avant de publier les exécutables.

- **CodeWalker** (dexyfex) : le dépôt n'a pas de licence explicite. 

Made with Claude Code
