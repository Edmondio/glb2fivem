// Langues de l'interface : français (par défaut si le système est en français) et anglais.
// Dans le code : t('texte français', 'English text'). Pour la page HTML : table HTML_EN ci-dessous.

const KEY = 'glb2fivem.lang';

export const lang = (() => {
  try {
    const saved = localStorage.getItem(KEY);
    if (saved === 'fr' || saved === 'en') return saved;
  } catch { /* stockage indisponible */ }
  return (navigator.language || '').toLowerCase().startsWith('fr') ? 'fr' : 'en';
})();

export const isEn = lang === 'en';
export const locale = isEn ? 'en-US' : 'fr-FR';

/** Texte dans la langue courante. */
export const t = (fr, en) => (isEn ? en : fr);

/** Nombre formaté (séparateurs de la langue). */
export const num = (v, opts) => Number(v).toLocaleString(locale, opts);

/** Change de langue : la page est rechargée. */
export function setLang(l) {
  try { localStorage.setItem(KEY, l); } catch { /* ignoré */ }
  location.reload();
}

// Textes fixes de index.html (français -> anglais).
const HTML_EN = {
  'Réglages': 'Settings', 'Lumières': 'Lights', 'Nettoyer': 'Clean up',
  'Nom du modèle': 'Model name', 'Minuscules, chiffres et _ uniquement.': 'Lowercase letters, digits and _ only.',
  'Taille et position': 'Size and position', 'Hauteur finale': 'Final height', 'Tourner de 90°': 'Rotate 90°',
  "Point d'origine": 'Origin point', 'Centre de la base (posé au sol)': 'Base center (on the ground)',
  'Centre du modèle': 'Model center', 'Origine du GLB': 'GLB origin',
  'Optimisation': 'Optimization', 'Triangles max': 'Max triangles', '10k léger': '10k light',
  '50k détaillé': '50k detailed', 'Taille max des textures': 'Max texture size',
  'Générer les LOD (moyen / bas / très bas)': 'Generate LODs (medium / low / very low)',
  'Boîte englobante (simple, léger)': 'Bounding box (simple, light)', 'Forme du modèle (précis)': 'Model shape (precise)',
  'Aucune': 'None', 'Détail de la collision': 'Collision detail',
  "Monte si des ouvertures (porte, barreaux) se bouchent : vérifie avec « Collision » dans l'aperçu.":
    'Raise it if openings (doors, bars) get closed: check with "Collision" in the preview.',
  'Avancé': 'Advanced', "Distance d'affichage (m)": 'Draw distance (m)',
  'Inverser le vert des normal maps (DirectX)': 'Flip normal map green channel (DirectX)',
  'Créer un aperçu pour CodeWalker': 'Create a CodeWalker preview', 'Garder les XML CodeWalker': 'Keep CodeWalker XML files',
  'Sortie': 'Output', 'Dossier des ressources': 'Resources folder',
  'Charge un modèle pour voir ses matériaux et textures.': 'Load a model to see its materials and textures.',
  '＋ Placer une lumière': '＋ Place a light', 'Aperçu de nuit': 'Night preview', 'Pas de la poignée': 'Gizmo step',
  'Libre': 'Free', '1 cm (rotation 15°)': '1 cm (15° rotation)', '5 cm (rotation 15°)': '5 cm (15° rotation)',
  '10 cm (rotation 15°)': '10 cm (15° rotation)',
  "Clique sur le modèle pour poser une lumière, puis ajuste-la avec la poignée (G : déplacer, R : orienter un spot) ou en tapant sa position. Clic sur une ampoule pour la sélectionner, Suppr pour l'effacer.":
    'Click the model to place a light, then adjust it with the gizmo (G: move, R: aim a spot) or by typing its position. Click a bulb to select it, Delete to remove it.',
  'Outil de sélection': 'Selection tool', 'Touche 1': 'Key 1', 'Touche 2': 'Key 2', 'Touche 3': 'Key 3',
  'Pièce': 'Part', 'Îlot': 'Island', 'Zone': 'Area',
  'Maj + clic : ajouter · Ctrl + clic : retirer · Échap : tout désélectionner': 'Shift + click: add · Ctrl + click: remove · Esc: clear selection',
  '🗑 Supprimer la sélection': '🗑 Delete selection', 'Suppr': 'Delete', 'Garder uniquement la sélection': 'Keep selection only',
  'Désélectionner': 'Clear selection', '↶ Annuler': '↶ Undo',
  "Modifications pas encore appliquées : la conversion utiliserait l'ancien modèle.": 'Changes not applied yet: the conversion would use the old model.',
  'Appliquer et recharger le modèle': 'Apply and reload the model',
  'Le modèle nettoyé est réexporté en GLB et rechargé (taille, LOD et collision sont recalculés). Fais le nettoyage avant de placer des lumières.':
    'The cleaned model is re-exported as GLB and reloaded (size, LODs and collision are recomputed). Clean up before placing lights.',
  'Convertir': 'Convert', 'Modèle affiché': 'Displayed model', 'Fil de fer': 'Wireframe', 'Mannequin': 'Mannequin',
  'Recadrer': 'Reframe', "Calcul de l'aperçu GTA…": 'Computing GTA preview…', 'Glisse un fichier .glb ici': 'Drop a .glb file here',
  'ou utilise « Ouvrir un .glb… »': 'or use "Open a .glb…"', "Lâche le fichier pour l'ouvrir": 'Release to open the file',
  'Conversion…': 'Converting…', 'Fermer': 'Close', 'Aucun modèle chargé': 'No model loaded', 'Ouvrir un .glb…': 'Open a .glb…',
  'Page GitHub du projet (sources, nouveautés)': 'Project GitHub page (sources, news)',
  'Peins, retouche, ajoute du texte ou des calques, puis applique : la texture du modèle est remplacée.':
    "Paint, retouch, add text or layers, then apply: the model's texture is replaced.",
  'Annuler': 'Cancel', '✓ Appliquer à la texture': '✓ Apply to texture', "Chargement de l'éditeur…": 'Loading editor…',
  'Langue': 'Language', 'Ped · 1,80 m': 'Ped · 1.80 m',
  // textes générés (onglets Textures / Lumières)
  'Couleur': 'Color',
  'Émissif': 'Emissive',
  'Normal map': 'Normal map',
  'Métal / rugosité': 'Metal / roughness',
  'Occlusion': 'Occlusion',
  'modifié': 'edited',
  'modifiée': 'edited',
  'Couleur de base (multiplie la texture)': 'Base color (multiplies the texture)',
  'Émissif (zones qui brillent)': 'Emissive (glowing areas)',
  'Toujours allumé': 'Always on',
  'La nuit seulement': 'Night only',
  'Désactivé': 'Off',
  'Intensité': 'Intensity',
  "Intégrer l'occlusion (ombres de contact) à la couleur": 'Bake occlusion (contact shadows) into the color',
  'Force': 'Strength',
  'Pas de texture : seule la couleur est utilisée.': 'No texture: only the color is used.',
  'Les retouches sont visibles dans la vue « Original » et intégrées au YTD à la conversion.': 'Edits are visible in the "Original" view and baked into the YTD on conversion.',
  'Luminosité': 'Brightness',
  'Contraste': 'Contrast',
  'Teinte': 'Hue',
  'Image technique : remplacement uniquement.': 'Technical image: replace only.',
  '✏ Éditer': '✏ Edit',
  'Remplacer…': 'Replace…',
  'Exporter PNG': 'Export PNG',
  'Réinitialiser': 'Reset',
  'Aucune lumière. Clique sur « Placer une lumière » puis sur le modèle.': 'No lights. Click "Place a light", then click the model.',
  'Supprimer': 'Delete',
  'Ponctuelle': 'Point',
  'Tube (néon)': 'Tube (neon)',
  'Raccourci : G': 'Shortcut: G',
  'Raccourci : R': 'Shortcut: R',
  '✥ Déplacer': '✥ Move',
  '⟳ Orienter': '⟳ Aim',
  'Taille': 'Size',
  'Longueur': 'Length',
  'Portée': 'Range',
  'Axe du tube': 'Tube axis',
  'Perpendiculaire à la surface': 'Perpendicular to the surface',
  'Vers le bas': 'Downward',
  'Personnalisée (poignée « Orienter »)': 'Custom ("Aim" gizmo)',
  'Décalage': 'Offset',
  'Allumage': 'Switch-on',
  'Toujours': 'Always',
  'La nuit seulement (20h → 6h)': 'Night only (8 pm → 6 am)',
  'Effet': 'Effect',
  'Projeter des ombres': 'Cast shadows',
  '(plus coûteux)': '(more expensive)',
  'Repositionner': 'Reposition',
  'Dupliquer': 'Duplicate',
};

/** Traduit les textes fixes de la page (nœuds texte, title, placeholder). */
export function translatePage(root = document.body) {
  document.documentElement.lang = lang;
  if (!isEn) return;
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    const key = n.nodeValue.trim();
    if (key && HTML_EN[key]) n.nodeValue = n.nodeValue.replace(key, HTML_EN[key]);
  }
  for (const el of root.querySelectorAll('[title],[placeholder]')) {
    for (const a of ['title', 'placeholder']) {
      const v = el.getAttribute(a);
      if (v && HTML_EN[v]) el.setAttribute(a, HTML_EN[v]);
    }
  }
}
