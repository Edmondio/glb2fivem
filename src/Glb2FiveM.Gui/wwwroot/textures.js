// Onglet « Textures » : retouche des images et des couleurs des matériaux, en direct dans le viewer.
// Les images retouchées sont envoyées au serveur au moment de la conversion.

import { openImageEditor } from './editor.js';
import { t as tr, translatePage, lang } from './i18n.js';

const SLOTS = [
  { key: 'map', label: 'Couleur', color: true },
  { key: 'emissiveMap', label: 'Émissif', color: true },
  { key: 'normalMap', label: 'Normal map', color: false },
  { key: 'roughnessMap', label: 'Métal / rugosité', color: false },
  { key: 'metalnessMap', label: 'Métal / rugosité', color: false },
  { key: 'aoMap', label: 'Occlusion', color: false },
];
const DEFAULT_ADJ = { brightness: 100, contrast: 100, saturation: 100, hue: 0 };
const MAX_SIZE = 4096;

const $ = (id) => document.getElementById(id);
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

const LABEL_EN = { 'Couleur': 'Color', 'Émissif': 'Emissive', 'Métal / rugosité': 'Metal / roughness' };
let images = new Map();     // index d'image glTF -> entrée
let materials = [];         // { mat, index, name, origHex, slots: [{label, image}] }
let replaceTarget = null;   // image en attente d'un fichier de remplacement

/** Analyse le glTF chargé : relie chaque texture three.js à son image glTF d'origine. */
export function initTextures(gltf) {
  images = new Map();
  materials = [];
  const parser = gltf.parser;
  const json = parser.json;

  // Texture three -> index d'image : par association directe, sinon par la source partagée (clones).
  const sourceToImage = new Map();
  const imageOf = (t) => {
    const a = parser.associations.get(t);
    let idx;
    if (a?.textures !== undefined) {
      const def = json.textures?.[a.textures];
      idx = def?.source ?? def?.extensions?.EXT_texture_webp?.source ?? def?.extensions?.KHR_texture_basisu?.source;
      if (idx !== undefined && t.source) sourceToImage.set(t.source, idx);
    }
    return idx ?? (t.source ? sourceToImage.get(t.source) : undefined);
  };

  const seen = new Set();
  const meshMats = [];
  gltf.scene.traverse((o) => {
    if (!o.isMesh) return;
    for (const m of Array.isArray(o.material) ? o.material : [o.material]) {
      if (!seen.has(m)) { seen.add(m); meshMats.push(m); }
    }
  });
  // Deux passes pour que les clones retrouvent leur image via la source.
  for (const pass of [0, 1]) {
    for (const m of meshMats) for (const s of SLOTS) if (m[s.key]) imageOf(m[s.key]);
    if (pass === 1) break;
  }

  for (const m of meshMats) {
    const assoc = parser.associations.get(m);
    const hasEmissive = !!m.emissiveMap || (m.emissive && (m.emissive.r + m.emissive.g + m.emissive.b) > 1e-4);
    const entry = {
      mat: m,
      index: assoc?.materials,
      // Réglages envoyés au convertisseur (valeurs par défaut = celles de R*).
      emissive: hasEmissive ? { mode: 'always', intensity: 5, base: m.emissiveIntensity ?? 1 } : null,
      ao: m.aoMap ? { bake: true, strength: 1 } : null,
      name: m.name || (assoc?.materials !== undefined ? tr(`matériau ${assoc.materials}`, `material ${assoc.materials}`) : tr('matériau par défaut', 'default material')),
      origHex: `#${m.color.getHexString()}`,
      slots: [],
    };
    const usedHere = new Set();
    for (const s of SLOTS) {
      const t = m[s.key];
      if (!t || !t.image) continue;
      const idx = imageOf(t);
      if (idx === undefined) continue;
      let img = images.get(idx);
      if (!img) {
        img = {
          index: idx,
          name: json.images?.[idx]?.name || `image ${idx}`,
          orig: t.image,
          replacement: null,
          canvas: null,
          adj: { ...DEFAULT_ADJ },
          color: s.color,
          textures: new Set(),
          labels: new Set(),
        };
        images.set(idx, img);
      }
      img.textures.add(t);
      img.labels.add(s.label);
      if (!s.color) img.color = false; // image aussi utilisée comme donnée technique
      if (!usedHere.has(idx)) { usedHere.add(idx); entry.slots.push({ label: s.label, image: img }); }
    }
    materials.push(entry);
  }
  render();
}

export function clearTextures() {
  images = new Map();
  materials = [];
  render();
}

/** Couleurs modifiées : { indexMatériau: [r, g, b, a] } en linéaire (comme glTF). */
export function colorOverrides() {
  const out = {};
  for (const e of materials) {
    if (e.index === undefined) continue;
    if (`#${e.mat.color.getHexString()}` === e.origHex) continue;
    out[e.index] = [e.mat.color.r, e.mat.color.g, e.mat.color.b, e.mat.opacity ?? 1];
  }
  return out;
}

/** Réglages émissif / occlusion par matériau glTF : { index: { emissive, emissiveIntensity, bakeAo, aoStrength } }. */
export function materialOptions() {
  const out = {};
  for (const e of materials) {
    if (e.index === undefined || (!e.emissive && !e.ao)) continue;
    out[e.index] = {
      emissive: e.emissive?.mode ?? 'always',
      emissiveIntensity: e.emissive?.intensity ?? 5,
      bakeAo: e.ao?.bake ?? true,
      aoStrength: e.ao?.strength ?? 1,
    };
  }
  return out;
}

/** Aperçu : l'émissif et l'occlusion du viewer suivent les réglages. */
function applyMaterialPreview(e) {
  if (e.emissive) e.mat.emissiveIntensity = e.emissive.mode === 'off' ? 0 : e.emissive.base * (e.emissive.intensity / 5);
  if (e.ao) e.mat.aoMapIntensity = e.ao.bake ? e.ao.strength : 0;
}

export function hasEdits() {
  return [...images.values()].some(isEdited) || Object.keys(colorOverrides()).length > 0;
}

/** Envoie au serveur les images retouchées (PNG pleine résolution). */
export async function uploadEdits(token, id) {
  const headers = { 'X-Token': token, 'X-Lang': lang };
  await fetch(`/api/texture-clear?id=${id}`, { method: 'POST', headers });
  for (const img of images.values()) {
    if (!isEdited(img)) continue;
    const blob = await new Promise((r) => img.canvas.toBlob(r, 'image/png'));
    const res = await fetch(`/api/texture?id=${id}&image=${img.index}`, { method: 'POST', headers, body: blob });
    if (!res.ok) throw new Error(`Envoi de la texture « ${img.name} » impossible`);
  }
}

// ------------------------------------------------------------------ retouche

function isEdited(img) {
  return !!img.replacement || Object.keys(DEFAULT_ADJ).some((k) => img.adj[k] !== DEFAULT_ADJ[k]);
}

function sourceOf(img) { return img.replacement ?? img.orig; }

/** Redessine l'image avec ses réglages et la pousse dans toutes les textures three qui l'utilisent. */
function apply(img) {
  if (!isEdited(img)) {
    for (const t of img.textures) { t.image = img.orig; t.needsUpdate = true; }
    img.canvas = null;
    updateThumb(img);
    return;
  }
  const src = sourceOf(img);
  let w = src.width, h = src.height;
  const k = Math.min(1, MAX_SIZE / Math.max(w, h));
  w = Math.max(1, Math.round(w * k));
  h = Math.max(1, Math.round(h * k));
  const c = img.canvas ?? document.createElement('canvas');
  if (c.width !== w || c.height !== h) { c.width = w; c.height = h; }
  const ctx = c.getContext('2d');
  ctx.clearRect(0, 0, w, h);
  const a = img.adj;
  ctx.filter = img.color ? `brightness(${a.brightness}%) contrast(${a.contrast}%) saturate(${a.saturation}%) hue-rotate(${a.hue}deg)` : 'none';
  ctx.drawImage(src, 0, 0, w, h);
  ctx.filter = 'none';
  img.canvas = c;
  for (const t of img.textures) { t.image = c; t.needsUpdate = true; }
  updateThumb(img);
}

let pending = new Set();
function scheduleApply(img) {
  if (pending.size === 0) requestAnimationFrame(() => { for (const i of pending) apply(i); pending = new Set(); });
  pending.add(img);
}

function updateThumb(img) {
  for (const cv of document.querySelectorAll(`canvas[data-thumb="${img.index}"]`)) {
    const ctx = cv.getContext('2d');
    ctx.clearRect(0, 0, cv.width, cv.height);
    ctx.drawImage(img.canvas ?? sourceOf(img), 0, 0, cv.width, cv.height);
  }
  for (const el of document.querySelectorAll(`[data-edited="${img.index}"]`)) el.hidden = !isEdited(img);
}

// ------------------------------------------------------------------ rendu de l'onglet

function render() {
  const list = $('texList');
  $('texCount').textContent = images.size ? `(${images.size})` : '';
  if (!materials.length) {
    list.innerHTML = '<p class="tex-empty muted">Charge un modèle pour voir ses matériaux et textures.</p>';
    translatePage(list);
    return;
  }
  list.innerHTML = materials.map((e, mi) => `
    <div class="mat">
      <div class="mat-head">
        <span class="mat-name" title="${esc(e.name)}">${esc(e.name)}</span>
        <span class="edited" data-color-edited="${mi}" hidden>modifié</span>
        ${e.index !== undefined ? `<input type="color" data-color="${mi}" value="${e.origHex}" title="Couleur de base (multiplie la texture)">` : ''}
      </div>
      ${e.emissive ? `
        <div class="mat-opt">
          <label class="field compact"><span>Émissif (zones qui brillent)</span>
            <select data-emissive="${mi}">
              <option value="always" ${e.emissive.mode === 'always' ? 'selected' : ''}>Toujours allumé</option>
              <option value="night" ${e.emissive.mode === 'night' ? 'selected' : ''}>La nuit seulement</option>
              <option value="off" ${e.emissive.mode === 'off' ? 'selected' : ''}>Désactivé</option>
            </select></label>
          <label class="slider">Intensité
            <input type="range" min="0" max="16" step="0.5" value="${e.emissive.intensity}" data-emissive-int="${mi}">
            <output>${e.emissive.intensity}</output>
          </label>
        </div>` : ''}
      ${e.ao ? `
        <div class="mat-opt">
          <label class="check"><input type="checkbox" data-ao="${mi}" ${e.ao.bake ? 'checked' : ''}> Intégrer l'occlusion (ombres de contact) à la couleur</label>
          <label class="slider">Force
            <input type="range" min="0" max="100" step="5" value="${Math.round(e.ao.strength * 100)}" data-ao-str="${mi}">
            <output>${Math.round(e.ao.strength * 100)}%</output>
          </label>
        </div>` : ''}
      ${e.slots.length ? e.slots.map((s) => imageRow(s)).join('') : '<p class="note">Pas de texture : seule la couleur est utilisée.</p>'}
    </div>`).join('') +
    '<p class="note" style="padding:10px 16px 16px">Les retouches sont visibles dans la vue « Original » et intégrées au YTD à la conversion.</p>';
  translatePage(list);
  for (const img of images.values()) updateThumb(img);
}

function imageRow(s) {
  const img = s.image;
  const src = sourceOf(img);
  const sliders = img.color ? [
    ['brightness', 'Luminosité', 0, 200, '%'],
    ['contrast', 'Contraste', 0, 200, '%'],
    ['saturation', 'Saturation', 0, 200, '%'],
    ['hue', 'Teinte', -180, 180, '°'],
  ].map(([k, label, min, max, unit]) => `
    <label class="slider">${label}
      <input type="range" min="${min}" max="${max}" step="1" value="${img.adj[k]}" data-adj="${k}" data-img="${img.index}">
      <output data-out="${k}-${img.index}">${img.adj[k]}${unit}</output>
    </label>`).join('') : '<p class="note">Image technique : remplacement uniquement.</p>';
  return `
    <div class="img-row" data-row="${img.index}">
      <div class="img-head" data-toggle="${img.index}">
        <canvas width="92" height="92" data-thumb="${img.index}"></canvas>
        <div class="img-info">
          <b>${esc(s.label)}</b> <span class="edited" data-edited="${img.index}" hidden>modifiée</span>
          <small>${esc(img.name)} · ${src.width}×${src.height}</small>
        </div>
        <span class="chev">›</span>
      </div>
      <div class="img-body">
        ${sliders}
        <div class="img-actions">
          <button class="btn btn-small" data-edit="${img.index}">✏ Éditer</button>
          <button class="btn btn-small" data-replace="${img.index}">Remplacer…</button>
          <button class="btn btn-small" data-download="${img.index}">Exporter PNG</button>
          <button class="btn btn-small" data-reset="${img.index}">Réinitialiser</button>
        </div>
      </div>
    </div>`;
}

// ------------------------------------------------------------------ événements

$('texList').addEventListener('click', async (e) => {
  const t = e.target.closest('[data-toggle],[data-edit],[data-replace],[data-download],[data-reset]');
  if (!t) return;
  if (t.dataset.toggle !== undefined) {
    t.closest('.img-row').classList.toggle('open');
  } else if (t.dataset.edit !== undefined) {
    const img = images.get(Number(t.dataset.edit));
    const row = t.closest('.img-row');
    openImageEditor({
      source: img.canvas ?? sourceOf(img),
      title: `${[...img.labels].map((x) => tr(x, LABEL_EN[x] ?? x)).join(' / ')} · ${img.name}`,
      onApply: async (canvas) => {
        // Le résultat de l'éditeur devient la nouvelle image ; les réglages déjà appliqués sont dedans.
        img.replacement = await createImageBitmap(canvas);
        img.adj = { ...DEFAULT_ADJ };
        apply(img);
        for (const input of row.querySelectorAll('input[data-adj]')) {
          input.value = img.adj[input.dataset.adj];
          row.querySelector(`[data-out="${input.dataset.adj}-${img.index}"]`).textContent = input.value + (input.dataset.adj === 'hue' ? '°' : '%');
        }
        row.querySelector('small').textContent = `${img.name} ${tr('(éditée)', '(edited)')} · ${canvas.width}×${canvas.height}`;
      },
    });
  } else if (t.dataset.replace !== undefined) {
    replaceTarget = images.get(Number(t.dataset.replace));
    $('imageInput').value = '';
    $('imageInput').click();
  } else if (t.dataset.download !== undefined) {
    const img = images.get(Number(t.dataset.download));
    const c = img.canvas ?? toCanvas(sourceOf(img));
    const a = document.createElement('a');
    a.href = c.toDataURL('image/png');
    a.download = `${img.name.replace(/[^\w.-]+/g, '_')}.png`;
    a.click();
  } else if (t.dataset.reset !== undefined) {
    const img = images.get(Number(t.dataset.reset));
    img.replacement = null;
    img.adj = { ...DEFAULT_ADJ };
    apply(img);
    const row = t.closest('.img-row');
    for (const input of row.querySelectorAll('input[data-adj]')) {
      input.value = img.adj[input.dataset.adj];
      row.querySelector(`[data-out="${input.dataset.adj}-${img.index}"]`).textContent = input.value + (input.dataset.adj === 'hue' ? '°' : '%');
    }
    row.querySelector('small').textContent = `${img.name} · ${img.orig.width}×${img.orig.height}`;
  }
});

$('texList').addEventListener('input', (e) => {
  const el = e.target;
  if (el.dataset.adj) {
    const img = images.get(Number(el.dataset.img));
    img.adj[el.dataset.adj] = Number(el.value);
    $('texList').querySelector(`[data-out="${el.dataset.adj}-${img.index}"]`).textContent = el.value + (el.dataset.adj === 'hue' ? '°' : '%');
    scheduleApply(img);
  } else if (el.dataset.emissiveInt !== undefined) {
    const e = materials[Number(el.dataset.emissiveInt)];
    e.emissive.intensity = Number(el.value);
    el.nextElementSibling.textContent = el.value;
    applyMaterialPreview(e);
  } else if (el.dataset.aoStr !== undefined) {
    const e = materials[Number(el.dataset.aoStr)];
    e.ao.strength = Number(el.value) / 100;
    el.nextElementSibling.textContent = `${el.value}%`;
    applyMaterialPreview(e);
  } else if (el.dataset.color !== undefined) {
    const entry = materials[Number(el.dataset.color)];
    entry.mat.color.set(el.value);
    $('texList').querySelector(`[data-color-edited="${el.dataset.color}"]`).hidden = el.value.toLowerCase() === entry.origHex.toLowerCase();
  }
});

$('texList').addEventListener('change', (ev) => {
  const el = ev.target;
  if (el.dataset.emissive !== undefined) {
    const e = materials[Number(el.dataset.emissive)];
    e.emissive.mode = el.value;
    applyMaterialPreview(e);
  } else if (el.dataset.ao !== undefined) {
    const e = materials[Number(el.dataset.ao)];
    e.ao.bake = el.checked;
    applyMaterialPreview(e);
  }
});

$('imageInput').addEventListener('change', async (e) => {
  const file = e.target.files[0];
  if (!file || !replaceTarget) return;
  try {
    replaceTarget.replacement = await createImageBitmap(file);
    apply(replaceTarget);
    const row = $('texList').querySelector(`[data-row="${replaceTarget.index}"]`);
    if (row) row.querySelector('small').textContent = `${file.name} · ${replaceTarget.replacement.width}×${replaceTarget.replacement.height}`;
  } catch {
    alert('Image illisible.');
  }
});

function toCanvas(src) {
  const c = document.createElement('canvas');
  c.width = src.width;
  c.height = src.height;
  c.getContext('2d').drawImage(src, 0, 0);
  return c;
}

for (const b of document.querySelectorAll('.tabs button')) {
  b.addEventListener('click', () => {
    for (const x of document.querySelectorAll('.tabs button')) x.classList.toggle('active', x === b);
    for (const tab of document.querySelectorAll('.tab')) tab.hidden = tab.id !== `tab-${b.dataset.tab}`;
    document.dispatchEvent(new CustomEvent('tabchange', { detail: b.dataset.tab }));
  });
}
