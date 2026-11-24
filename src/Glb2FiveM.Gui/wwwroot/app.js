import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { initTextures, colorOverrides, uploadEdits, materialOptions } from './textures.js';
import { initLights, clearLights, lightsPayload, refreshLights, tickLights, lightCount } from './lights.js';
import { initCleanup, resetCleanup, isCleanupDirty } from './cleanup.js';
import { t, num, lang, setLang, translatePage } from './i18n.js';

const token = new URLSearchParams(location.search).get('t') ?? '';
const $ = (id) => document.getElementById(id);
const fmt = (n, d = 2) => num(n, { minimumFractionDigits: d, maximumFractionDigits: d });
const fmtInt = (n) => num(n);

const PED_HEIGHT = 1.8;
const HEIGHT_MIN = 0.05, HEIGHT_MAX = 50;

const state = {
  file: null,          // File
  buffer: null,        // ArrayBuffer du GLB
  model: null,         // scène glTF
  rawSize: null,       // taille d'origine (Vector3, mètres glTF)
  triangles: 0,
  materials: 0,
  textures: 0,
  height: 1,
  nameTouched: false,
  uploadId: null,      // fichier envoyé au serveur local
  analysis: null,      // géométrie finale calculée par le convertisseur (LOD + collision)
  analysisSeq: 0,
  view: 'original',    // 'original' ou index de LOD (0..3)
};

// ------------------------------------------------------------------ scène 3D

const canvas = $('canvas');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true }); // fond transparent : dégradé CSS
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;

const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(40, 1, 0.01, 2000);
camera.position.set(2.5, 1.8, 3.5);
const controls = new OrbitControls(camera, canvas);
controls.enableDamping = true;
controls.target.set(0, 0.9, 0);

const hemi = new THREE.HemisphereLight(0xdfe8ff, 0x3a3328, 1.6);
scene.add(hemi);
const sun = new THREE.DirectionalLight(0xffffff, 2.2);
sun.position.set(3, 6, 4);
scene.add(sun);
const fill = new THREE.DirectionalLight(0xffffff, 0.6);
fill.position.set(-4, 2, -3);
scene.add(fill);

// Sol : grille fine tous les 0,5 m, grille marquée tous les mètres.
const gridFine = new THREE.GridHelper(20, 40, 0x30353f, 0x30353f);
const gridMain = new THREE.GridHelper(20, 20, 0x5a6480, 0x414858);
gridMain.position.y = 0.001;
scene.add(gridFine, gridMain);

// Mannequin de 1,80 m (silhouette générique).
const ped = new THREE.Group();
{
  const mat = new THREE.MeshStandardMaterial({ color: 0x7d8cb0, roughness: 0.8 });
  const box = (x, y0, w, h, d, z = 0) => {
    const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
    m.position.set(x, y0 + h / 2, z);
    ped.add(m);
  };
  box(-0.1, 0, 0.15, 0.86, 0.17);         // jambes
  box(0.1, 0, 0.15, 0.86, 0.17);
  box(0, 0.86, 0.44, 0.6, 0.24);          // torse
  box(-0.28, 0.85, 0.1, 0.6, 0.12);       // bras
  box(0.28, 0.85, 0.1, 0.6, 0.12);
  box(0, 1.46, 0.1, 0.08, 0.1);           // cou
  box(0, 1.54, 0.2, 0.26, 0.23, 0.01);    // tête -> 1,80 m
}
scene.add(ped);

const modelRoot = new THREE.Group(); // reçoit rotation / échelle / pivot
scene.add(modelRoot);

// Géométrie finale calculée par le convertisseur (coordonnées GTA -> three : (x, y, z) -> (x, z, -y)).
const gtaRoot = new THREE.Group();
const lodGroup = new THREE.Group();
const collisionGroup = new THREE.Group();
collisionGroup.visible = false;
collisionGroup.renderOrder = 2;
gtaRoot.add(lodGroup, collisionGroup);
scene.add(gtaRoot);
const clayMat = new THREE.MeshStandardMaterial({ color: 0xb9bdc7, roughness: 0.75, metalness: 0.05, side: THREE.DoubleSide });
const collFill = new THREE.MeshBasicMaterial({ color: 0xff8a2a, transparent: true, opacity: 0.28, depthWrite: false, side: THREE.DoubleSide });
const collLine = new THREE.LineBasicMaterial({ color: 0xffa64d, transparent: true, opacity: 0.9 });

function resize() {
  const r = $('viewer').getBoundingClientRect();
  renderer.setSize(r.width, r.height, false);
  camera.aspect = r.width / Math.max(1, r.height);
  camera.updateProjectionMatrix();
}
new ResizeObserver(resize).observe($('viewer'));
resize();

const tmp = new THREE.Vector3();
const clock = new THREE.Clock();
renderer.setAnimationLoop(() => {
  controls.update();
  tickLights(clock.getElapsedTime());
  renderer.render(scene, camera);
  // Étiquette au-dessus de la tête du mannequin.
  const label = $('pedLabel');
  if (ped.visible) {
    tmp.set(ped.position.x, PED_HEIGHT + 0.08, ped.position.z).project(camera);
    const r = canvas.getBoundingClientRect();
    label.style.left = `${(tmp.x * 0.5 + 0.5) * r.width}px`;
    label.style.top = `${(-tmp.y * 0.5 + 0.5) * r.height}px`;
    label.hidden = tmp.z > 1;
  } else label.hidden = true;
});

// ------------------------------------------------------------------ chargement

const loader = new GLTFLoader();

/** @param keepScale facteur mètres/unité à conserver (rechargement après nettoyage). */
async function loadFile(file, keepScale = null) {
  if (!file) return;
  if (!/\.(glb|gltf)$/i.test(file.name)) return toast(t(`« ${file.name} » n'est pas un .glb`, `"${file.name}" is not a .glb`));
  setBusy(true, t('Lecture du modèle…', 'Reading model…'));
  try {
    const buffer = await file.arrayBuffer();
    const gltf = await loader.parseAsync(buffer, '');
    setModel(file, buffer, gltf.scene, keepScale);
    initTextures(gltf);
    resetCleanup();
    setBusy(true, t('Préparation…', 'Preparing…'));
    const up = await fetch(`/api/upload?file=${encodeURIComponent(file.name)}`, {
      method: 'POST',
      headers: { 'X-Token': token, 'X-Lang': lang, 'Content-Type': 'application/octet-stream' },
      body: buffer,
    }).then((r) => r.json());
    if (up.error) throw new Error(up.error);
    state.uploadId = up.id;
    $('convert').disabled = false;
    analyze();
  } catch (e) {
    console.error(e);
    let msg = friendlyError(e);
    if (/DRACO|meshopt|KHR_draco|EXT_meshopt/i.test(msg)) msg = t('Ce GLB est compressé (Draco / meshopt) : pas encore pris en charge. Ré-exporte-le sans compression.', 'This GLB is compressed (Draco / meshopt): not supported yet. Re-export it without compression.');
    showResult(`<h3 class="err">${t('Impossible de lire le fichier', 'Could not read the file')}</h3><p>${esc(msg)}</p>`);
  } finally {
    setBusy(false);
  }
}

function setModel(file, buffer, model, keepScale = null) {
  if (state.model) {
    modelRoot.remove(state.model);
    disposeTree(state.model);
  }
  state.file = file;
  state.buffer = buffer;
  state.model = model;
  state.uploadId = null;
  state.analysis = null;
  clearLights();
  clearGroup(lodGroup);
  clearGroup(collisionGroup);
  setView('original');

  let tris = 0;
  const mats = new Set(), texs = new Set();
  model.traverse((o) => {
    if (!o.isMesh) return;
    const g = o.geometry;
    tris += (g.index ? g.index.count : g.attributes.position.count) / 3;
    for (const m of Array.isArray(o.material) ? o.material : [o.material]) {
      mats.add(m);
      for (const k of ['map', 'normalMap', 'roughnessMap', 'metalnessMap', 'emissiveMap']) if (m[k]) texs.add(m[k]);
    }
  });
  state.triangles = Math.round(tris);
  state.materials = mats.size;
  state.textures = texs.size;

  modelRoot.rotation.set(0, 0, 0);
  modelRoot.scale.set(1, 1, 1);
  modelRoot.position.set(0, 0, 0);
  modelRoot.add(model);
  modelRoot.updateMatrixWorld(true); // applique la remise à zéro du parent avant de mesurer
  state.rawSize = new THREE.Box3().setFromObject(model).getSize(new THREE.Vector3());

  // Hauteur de départ : celle du modèle, sauf si elle semble être en centimètres / millimètres.
  let h = state.rawSize.y;
  let hint = '';
  if (h > 50) {
    h = h / 100;
    hint = t(`Modèle de ${fmt(state.rawSize.y, 1)} m à l'origine : probablement en centimètres, ramené à ${fmt(h)} m.`, `Model is ${fmt(state.rawSize.y, 1)} m originally: probably in centimetres, scaled to ${fmt(h)} m.`);
    if (h > 50) { h = h / 10; hint = t(`Modèle très grand à l'origine (${fmt(state.rawSize.y, 0)}) : ramené à ${fmt(h)} m, ajuste si besoin.`, `Model is very large originally (${fmt(state.rawSize.y, 0)}): scaled to ${fmt(h)} m, adjust if needed.`); }
  } else if (h < 0.01) {
    h = 1;
    hint = t('Modèle minuscule à l\'origine : mis à 1 m, ajuste si besoin.', 'Model is tiny originally: set to 1 m, adjust if needed.');
  }
  if (keepScale) { h = state.rawSize.y * keepScale; hint = ''; }
  $('scaleHint').textContent = hint;
  $('scaleHint').classList.toggle('warn', !!hint);
  setHeight(clamp(h, 0.01, 200));
  $('rotate').value = 0;

  if (!state.nameTouched || !$('name').value) $('name').value = sanitizeName(file.name.replace(/(_propre)?\.(glb|gltf)$/i, ''));
  validateName();

  $('fileLabel').innerHTML = `<b>${esc(file.name)}</b> · ${fmt(file.size / 1048576, 1)} ${t('Mo', 'MB')}`;
  $('empty').hidden = true;
  $('convert').textContent = t('Convertir', 'Convert');
  $('result').hidden = true;
  applyWireframe();
  updateTransform();
  frameAll();
}

function disposeTree(obj) {
  obj.traverse((o) => {
    o.geometry?.dispose?.();
    const ms = Array.isArray(o.material) ? o.material : o.material ? [o.material] : [];
    for (const m of ms) {
      for (const v of Object.values(m)) if (v?.isTexture) v.dispose();
      m.dispose();
    }
  });
}

// ------------------------------------------------------------------ transformation

function updateTransform() {
  if (!state.model) return;
  const rot = Number($('rotate').value);
  $('rotateValue').textContent = `${rot}°`;

  const s = state.height / Math.max(1e-6, state.rawSize.y);
  modelRoot.position.set(0, 0, 0);
  modelRoot.rotation.set(0, THREE.MathUtils.degToRad(rot), 0);
  modelRoot.scale.setScalar(s);
  modelRoot.updateMatrixWorld(true);

  const box = new THREE.Box3().setFromObject(modelRoot);
  const center = box.getCenter(new THREE.Vector3());
  switch ($('pivot').value) {
    case 'base': modelRoot.position.set(-center.x, -box.min.y, -center.z); break;
    case 'center': modelRoot.position.set(-center.x, -center.y, -center.z); break;
    default: break; // origine du GLB
  }
  modelRoot.updateMatrixWorld(true);
  box.setFromObject(modelRoot);

  // Mannequin à droite du prop.
  ped.position.set(box.max.x + 0.55, 0, (box.min.z + box.max.z) / 2);

  state.previewSize = box.getSize(new THREE.Vector3());
  renderStats();
  refreshLights();
}

function setHeight(h, fromSlider = false) {
  state.height = h;
  $('heightValue').textContent = `${fmt(h)} m`;
  if (document.activeElement !== $('height')) $('height').value = Number(h.toFixed(2));
  if (!fromSlider) {
    const t = (Math.log10(clamp(h, HEIGHT_MIN, HEIGHT_MAX)) - Math.log10(HEIGHT_MIN)) / (Math.log10(HEIGHT_MAX) - Math.log10(HEIGHT_MIN));
    $('heightSlider').value = Math.round(t * 1000);
  }
  updateTransform();
}

function frameAll() {
  const box = new THREE.Box3().setFromObject(modelRoot);
  if (ped.visible) box.expandByObject(ped);
  const size = box.getSize(new THREE.Vector3());
  const center = box.getCenter(new THREE.Vector3());
  const radius = Math.max(size.length() / 2, 0.3);
  const dist = radius / Math.sin(THREE.MathUtils.degToRad(camera.fov / 2)) * 1.05;
  const dir = new THREE.Vector3(0.55, 0.35, 1).normalize();
  camera.position.copy(center).addScaledVector(dir, dist);
  camera.near = dist / 200;
  camera.far = dist * 50;
  camera.updateProjectionMatrix();
  controls.target.copy(center);
}

function applyWireframe() {
  const on = $('wireframe').checked;
  clayMat.wireframe = on;
  state.model?.traverse((o) => {
    if (!o.isMesh) return;
    for (const m of Array.isArray(o.material) ? o.material : [o.material]) m.wireframe = on;
  });
}

// ------------------------------------------------------------------ conversion

async function convert() {
  if (!state.uploadId) return;
  if (!validateName()) return $('name').focus();
  const params = new URLSearchParams({
    ...geometryParams(),
    name: $('name').value.trim(),
    maxTexture: $('maxTexture').value,
    flipNormal: $('flipNormal').checked ? '1' : '0',
    keepXml: $('keepXml').checked ? '1' : '0',
    cwPreview: $('cwPreview').checked ? '1' : '0',
    lodDist: $('lodDist').value,
    out: $('outDir').value.trim(),
  });
  const colors = colorOverrides();
  if (Object.keys(colors).length) params.set('colors', JSON.stringify(colors));
  setBusy(true, t('Conversion en cours…', 'Converting…'));
  try {
    setBusy(true, t('Envoi des textures retouchées…', 'Uploading edited textures…'));
    await uploadEdits(token, state.uploadId);
    setBusy(true, t('Conversion en cours…', 'Converting…'));
    const res = await fetch(`/api/convert?${params}`, {
      method: 'POST',
      headers: { 'X-Token': token, 'X-Lang': lang, 'Content-Type': 'application/json' },
      body: JSON.stringify({ lights: lightsPayload(), materials: materialOptions() }),
    });
    const data = await res.json();
    if (!res.ok || data.error) return showResult(renderError(data));
    showResult(renderSuccess(data));
    localStorage.setItem('glb2fivem.outDir', $('outDir').value.trim());
  } catch (e) {
    showResult(`<h3 class="err">${t('Échec', 'Failed')}</h3><p>${esc(friendlyError(e))}</p>`);
  } finally {
    setBusy(false);
  }
}

function renderSuccess(d) {
  const warnings = d.logs.filter((l) => l.level !== 'info');
  const lods = d.lods.map((l) => `<tr><td>${l.level}</td><td class="r">${fmtInt(l.triangles)}</td><td class="r">${fmtInt(l.vertices)}</td><td class="r">${l.distance < 9000 ? Math.round(l.distance) + ' m' : '—'}</td></tr>`).join('');
  const texs = d.textures.map((t) => `<tr><td>${esc(t.name)}</td><td>${t.width}×${t.height}</td><td>${t.format}</td></tr>`).join('');
  const files = d.files.map((f) => `<tr><td>${esc(f.name)}</td><td class="r">${fmt(f.bytes / 1024, 1)} ${t('Ko', 'KB')}</td></tr>`).join('');
  const spawn = `local c = GetEntityCoords(PlayerPedId())\nlocal obj = CreateObject(\`${d.propName}\`, c.x, c.y + 1.0, c.z - 1.0, true, true, false)\nFreezeEntityPosition(obj, true) -- ${t('indispensable pour la collision « forme du modèle »', 'required for "model shape" collision')}`;
  return `
    <h3 class="ok">✓ ${esc(d.propName)} ${t('prêt', 'ready')}</h3>
    <div class="muted">${fmt(d.size[0])} × ${fmt(d.size[1])} × ${fmt(d.size[2])} m · ${d.seconds} s${d.lights ? ` · ${d.lights} ${t('lumière(s)', 'light(s)')}` : ''}${d.decimated ? ` · ${t('réduit depuis', 'reduced from')} ${fmtInt(d.sourceTriangles)} triangles` : ''}</div>
    ${warnings.length ? `<h4>${t('Avertissements', 'Warnings')}</h4><ul class="log">${warnings.map((w) => `<li class="${w.level}">${esc(w.message)}</li>`).join('')}</ul>` : ''}
    <h4>LOD</h4>
    <table><tr><th>${t('Niveau', 'Level')}</th><th class="r">Triangles</th><th class="r">Vertices</th><th class="r">${t('Jusqu\'à', 'Up to')}</th></tr>${lods}</table>
    <h4>Textures</h4>
    <table>${texs}</table>
    <h4>${t('Fichiers', 'Files')}</h4>
    <table>${files}</table>
    ${d.previewDir ? `
    <h4>${t('Voir le rendu du jeu dans CodeWalker', 'See the in-game look in CodeWalker')}</h4>
    <ol class="steps">${t(`
      <li>Ouvre CodeWalker en mode <b>RPF Explorer</b>, puis <b>File → Open Folder…</b> et choisis le dossier d'aperçu.</li>
      <li>Double-clique sur <b>${esc(d.propName)}.ydr</b> (textures et lumières sont intégrées).</li>
      <li>Clique sur <b>&gt;&gt;</b> en haut à gauche, onglet <b>Options → Lighting</b> : coche <b>Deferred shading</b>, <b>HD lights</b> et <b>Skydome</b>, puis mets <b>Time of day</b> vers 22:00.</li>`, `
      <li>Open CodeWalker in <b>RPF Explorer</b> mode, then <b>File → Open Folder…</b> and pick the preview folder.</li>
      <li>Double-click <b>${esc(d.propName)}.ydr</b> (textures and lights are embedded).</li>
      <li>Click <b>&gt;&gt;</b> at the top left, <b>Options → Lighting</b> tab: tick <b>Deferred shading</b>, <b>HD lights</b> and <b>Skydome</b>, then set <b>Time of day</b> to about 22:00.</li>`)}
    </ol>
    <div class="actions"><button class="btn" data-open="${esc(d.previewDir)}">📂 ${t('Dossier d\'aperçu CodeWalker', 'CodeWalker preview folder')}</button></div>` : ''}
    <h4>${t('Utilisation', 'Usage')}</h4>
    <code>ensure ${esc(d.propName)}</code>
    <code>${esc(spawn)}</code>
    <div class="actions">
      <button class="btn" data-open="${esc(d.resourceDir)}">📂 ${t('Ouvrir le dossier', 'Open folder')}</button>
      <button class="btn" data-copy="ensure ${esc(d.propName)}">${t('Copier « ensure »', 'Copy "ensure"')}</button>
      <button class="btn" data-copy="${esc(spawn)}">${t('Copier le spawn', 'Copy spawn code')}</button>
    </div>`;
}

function renderError(d) {
  const logs = (d.logs ?? []).map((l) => `<li class="${l.level}">${esc(l.message)}</li>`).join('');
  return `<h3 class="err">${t('Échec de la conversion', 'Conversion failed')}</h3><p>${esc(d.error ?? t('Erreur inconnue', 'Unknown error'))}</p>${logs ? `<h4>${t('Journal', 'Log')}</h4><ul class="log">${logs}</ul>` : ''}`;
}

function showResult(html) {
  $('resultBody').innerHTML = html;
  $('result').hidden = false;
}

$('result').addEventListener('click', async (e) => {
  const b = e.target.closest('button');
  if (!b) return;
  if (b.dataset.open) {
    await api('/api/open', { path: b.dataset.open });
  } else if (b.dataset.copy) {
    await navigator.clipboard.writeText(b.dataset.copy).catch(() => {});
    const old = b.textContent;
    b.textContent = t('Copié ✓', 'Copied ✓');
    setTimeout(() => (b.textContent = old), 1200);
  }
});

// ------------------------------------------------------------------ aperçu GTA (LOD + collision)

/** Paramètres qui changent la géométrie finale (partagés par l'aperçu et la conversion). */
function geometryParams() {
  return {
    id: state.uploadId ?? '',
    height: String(state.height),
    rotate: $('rotate').value,
    pivot: $('pivot').value,
    maxTris: $('maxTris').value,
    lods: $('lods').checked ? '1' : '0',
    collision: $('collision').value,
    collisionTris: String(collisionTris()),
  };
}

let analyzeTimer = 0;
function scheduleAnalyze(delay = 350) {
  if (!state.uploadId) return;
  clearTimeout(analyzeTimer);
  analyzeTimer = setTimeout(analyze, delay);
}

async function analyze() {
  if (!state.uploadId) return;
  const seq = ++state.analysisSeq;
  $('analysisBadge').hidden = false;
  try {
    const res = await fetch(`/api/analyze?${new URLSearchParams(geometryParams())}`, { method: 'POST', headers: { 'X-Token': token, 'X-Lang': lang } });
    const data = await res.json();
    if (seq !== state.analysisSeq) return; // une analyse plus récente est en route
    if (!res.ok || data.error) throw new Error(data.error ?? res.statusText);
    state.analysis = data;
    buildAnalysisMeshes(data);
    renderStats();
  } catch (e) {
    if (seq === state.analysisSeq) {
      state.analysis = { error: friendlyError(e) };
      renderStats();
    }
  } finally {
    if (seq === state.analysisSeq) $('analysisBadge').hidden = true;
  }
}

function decodeF32(b64) { return new Float32Array(Uint8Array.from(atob(b64), (c) => c.charCodeAt(0)).buffer); }
function decodeU32(b64) { return new Uint32Array(Uint8Array.from(atob(b64), (c) => c.charCodeAt(0)).buffer); }

function gtaGeometry(posB64, idxB64) {
  const p = decodeF32(posB64);
  for (let i = 0; i < p.length; i += 3) { const y = p[i + 1]; p[i + 1] = p[i + 2]; p[i + 2] = -y; }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(p, 3));
  g.setIndex(new THREE.BufferAttribute(decodeU32(idxB64), 1));
  g.computeVertexNormals();
  g.computeBoundingSphere();
  return g;
}

function clearGroup(group) {
  for (const c of [...group.children]) {
    group.remove(c);
    c.traverse((o) => o.geometry?.dispose?.());
  }
}

function buildAnalysisMeshes(a) {
  clearGroup(lodGroup);
  clearGroup(collisionGroup);
  const levels = ['High', 'Medium', 'Low', 'VeryLow'];
  for (const lod of a.lods) {
    const m = new THREE.Mesh(gtaGeometry(lod.positions, lod.indices), clayMat);
    m.userData.lod = levels.indexOf(lod.level);
    lodGroup.add(m);
  }
  const c = a.collision;
  if (c?.type === 'mesh') {
    const g = gtaGeometry(c.positions, c.indices);
    collisionGroup.add(new THREE.Mesh(g, collFill));
    collisionGroup.add(new THREE.LineSegments(new THREE.WireframeGeometry(g), collLine));
  } else if (c?.type === 'box') {
    // min/max en coordonnées GTA -> boîte three
    const min = new THREE.Vector3(c.min[0], c.min[2], -c.max[1]);
    const max = new THREE.Vector3(c.max[0], c.max[2], -c.min[1]);
    const size = max.clone().sub(min), center = min.clone().add(max).multiplyScalar(0.5);
    const g = new THREE.BoxGeometry(Math.max(size.x, 0.02), Math.max(size.y, 0.02), Math.max(size.z, 0.02));
    g.translate(center.x, center.y, center.z);
    collisionGroup.add(new THREE.Mesh(g, collFill));
    collisionGroup.add(new THREE.LineSegments(new THREE.EdgesGeometry(g), collLine));
  }
  // Boutons LOD disponibles
  const seg = $('viewSource');
  seg.querySelectorAll('button[data-v]:not([data-v="original"])').forEach((b) => b.remove());
  for (const lod of a.lods) {
    const b = document.createElement('button');
    b.dataset.v = String(levels.indexOf(lod.level));
    b.textContent = lod.level;
    seg.appendChild(b);
  }
  if (state.view !== 'original' && !a.lods.some((l) => levels.indexOf(l.level) === state.view)) state.view = 'original';
  setView(state.view);
}

function setView(v) {
  state.view = v;
  modelRoot.visible = v === 'original';
  for (const m of lodGroup.children) m.visible = m.userData.lod === v;
  for (const b of $('viewSource').querySelectorAll('button')) b.classList.toggle('active', b.dataset.v === String(v));
  renderStats();
}

function renderStats() {
  if (!state.model) return;
  const size = state.previewSize ?? new THREE.Vector3();
  const vsPed = size.y / PED_HEIGHT;
  const a = state.analysis;
  let rows = `<tr class="${state.view === 'original' ? 'current' : ''}"><td>Original</td><td class="r">${fmtInt(state.triangles)}</td><td class="r">${a?.sourceVertices ? fmtInt(a.sourceVertices) : '—'}</td></tr>`;
  if (a?.lods) {
    a.lods.forEach((l, i) => {
      rows += `<tr class="${state.view === i ? 'current' : ''}"><td>${l.level}</td><td class="r">${fmtInt(l.triangles)}</td><td class="r">${fmtInt(l.vertices)}</td></tr>`;
    });
    const c = a.collision;
    rows += c
      ? `<tr class="coll"><td>Collision</td><td class="r">${fmtInt(c.triangles)}</td><td class="r">${c.type === 'box' ? t('boîte', 'box') : fmtInt(c.vertices)}</td></tr>`
      : `<tr class="coll"><td>Collision</td><td class="r" colspan="2">${t('aucune', 'none')}</td></tr>`;
  }
  const stale = isCleanupDirty() ? `<div class="warn">${t('LOD et collision affichés : ceux d\'avant le nettoyage (applique-le pour les recalculer).', 'LODs and collision shown are from before the cleanup (apply it to recompute them).')}</div>` : '';
  const warn = stale + (a?.error ? `<div class="warn">${t('Aperçu indisponible', 'Preview unavailable')} : ${esc(a.error)}</div>`
    : (a?.logs ?? []).map((l) => `<div class="warn">⚠ ${esc(l.message)}</div>`).join(''));
  $('stats').innerHTML =
    `<div class="big">${fmt(size.x)} × ${fmt(size.z)} × ${fmt(size.y)} m</div>` +
    `<div class="muted">${t('largeur × profondeur × hauteur', 'width × depth × height')} · ${vsPed >= 0.1 ? Math.round(vsPed * 100) + t(' % d\'un ped', '% of a ped') : t('plus petit qu\'une main', 'smaller than a hand')}</div>` +
    `<table><tr class="muted"><td></td><td class="r">Triangles</td><td class="r">Vertices</td></tr>${rows}</table>` +
    `<div class="muted">${state.materials} ${t('matériau(x)', 'material(s)')} · ${state.textures} texture(s)</div>` + warn;
  $('stats').hidden = false;
}

// Détail de la collision : curseur logarithmique 300 -> 20 000 triangles.
const CT_MIN = 300, CT_MAX = 20000;
function collisionTris() {
  const t = $('collisionTris').value / 1000;
  return Math.round(10 ** (Math.log10(CT_MIN) + t * (Math.log10(CT_MAX) - Math.log10(CT_MIN))) / 50) * 50;
}
function setCollisionTris(n) {
  $('collisionTris').value = Math.round((Math.log10(n) - Math.log10(CT_MIN)) / (Math.log10(CT_MAX) - Math.log10(CT_MIN)) * 1000);
  $('collisionTrisValue').textContent = `${fmtInt(collisionTris())} triangles`;
}

// ------------------------------------------------------------------ utilitaires

/** « Failed to fetch » = le programme local ne répond plus. */
function friendlyError(e) {
  const msg = e?.message ?? String(e);
  if (e instanceof TypeError && /fetch|network/i.test(msg))
    return t('Le programme glb2fivem ne répond plus (il a été fermé ou s\'est arrêté). Relance-le. Si ça recommence sur ce modèle, envoie le fichier %LOCALAPPDATA%\\glb2fivem\\erreurs.txt.', 'glb2fivem is not responding (it was closed or stopped). Restart it. If it happens again with this model, send the file %LOCALAPPDATA%\\glb2fivem\\erreurs.txt.');
  return msg;
}

async function api(path, body) {
  const res = await fetch(path, {
    method: body ? 'POST' : 'GET',
    headers: { 'X-Token': token, 'X-Lang': lang, 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined,
  });
  return res.json();
}

function setBusy(on, text) {
  $('busy').hidden = !on;
  if (text) $('busyText').textContent = text;
  $('convert').disabled = on || !state.uploadId || isCleanupDirty();
}

function toast(msg) { showResult(`<p>${esc(msg)}</p>`); }
function clamp(v, a, b) { return Math.min(b, Math.max(a, v)); }
function esc(s) { return String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

/** Même règle que le convertisseur : minuscules, [a-z0-9_], commence par une lettre. */
function sanitizeName(raw) {
  let s = raw.trim().toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
  s = s.replace(/[^a-z0-9]/g, '_').replace(/_+/g, '_').replace(/^_|_$/g, '');
  if (!s) s = 'prop';
  if (!/^[a-z]/.test(s)) s = 'prop_' + s;
  return s.slice(0, 48).replace(/_$/, '');
}

function validateName() {
  const v = $('name').value.trim();
  const ok = /^[a-z][a-z0-9_]{0,47}$/.test(v);
  $('name').classList.toggle('invalid', !ok);
  $('nameHint').textContent = ok ? t('Minuscules, chiffres et _ uniquement.', 'Lowercase letters, digits and _ only.') : t(`Nom invalide, suggestion : ${sanitizeName(v || 'prop')}`, `Invalid name, suggestion: ${sanitizeName(v || 'prop')}`);
  $('nameHint').classList.toggle('warn', !ok);
  return ok;
}

// ------------------------------------------------------------------ événements

$('fileInput').addEventListener('change', (e) => loadFile(e.target.files[0]));

let dragDepth = 0;
addEventListener('dragenter', (e) => { e.preventDefault(); dragDepth++; document.body.classList.add('dragging'); });
addEventListener('dragleave', () => { if (--dragDepth <= 0) { dragDepth = 0; document.body.classList.remove('dragging'); } });
addEventListener('dragover', (e) => e.preventDefault());
addEventListener('drop', (e) => {
  e.preventDefault();
  dragDepth = 0;
  document.body.classList.remove('dragging');
  loadFile(e.dataTransfer.files[0]);
});

$('heightSlider').addEventListener('input', (e) => {
  const t = e.target.value / 1000;
  const h = 10 ** (Math.log10(HEIGHT_MIN) + t * (Math.log10(HEIGHT_MAX) - Math.log10(HEIGHT_MIN)));
  setHeight(Number(h.toPrecision(3)), true);
});
$('height').addEventListener('input', (e) => {
  const h = parseFloat(String(e.target.value).replace(',', '.'));
  if (h > 0) setHeight(h);
});
$('rotate').addEventListener('input', updateTransform);
$('rotate').addEventListener('change', () => scheduleAnalyze());
// Recadrage + recalcul de l'aperçu quand on relâche le réglage de taille.
$('heightSlider').addEventListener('change', () => { frameAll(); scheduleAnalyze(); });
$('height').addEventListener('change', () => { frameAll(); scheduleAnalyze(); });
$('lods').addEventListener('change', () => scheduleAnalyze(0));
$('collisionTris').addEventListener('input', () => { $('collisionTrisValue').textContent = `${fmtInt(collisionTris())} triangles`; });
$('collisionTris').addEventListener('change', () => scheduleAnalyze(0));
$('showCollision').addEventListener('change', (e) => { collisionGroup.visible = e.target.checked; });
$('viewSource').addEventListener('click', (e) => {
  const b = e.target.closest('button');
  if (!b) return;
  setView(b.dataset.v === 'original' ? 'original' : Number(b.dataset.v));
});
$('rotate90').addEventListener('click', () => {
  let r = Number($('rotate').value) + 90;
  if (r > 180) r -= 360;
  $('rotate').value = r;
  updateTransform();
  scheduleAnalyze();
});
$('pivot').addEventListener('change', () => { updateTransform(); scheduleAnalyze(0); });
const collisionHints = {
  box: t('Une boîte autour du prop : idéal pour caisses, meubles pleins, panneaux.', 'A box around the prop: ideal for crates, solid furniture, signs.'),
  mesh: t('Suit la forme : tables, chaises, cages, arches, objets creux.', 'Follows the shape: tables, chairs, cages, arches, hollow objects.'),
  none: t('Le prop sera traversable.', 'The prop can be walked through.'),
};
$('collision').addEventListener('change', () => {
  const v = $('collision').value;
  $('collisionHint').textContent = collisionHints[v];
  $('collisionTrisField').hidden = v !== 'mesh';
  if (v !== 'none') { $('showCollision').checked = true; collisionGroup.visible = true; }
  scheduleAnalyze(0);
});
$('collisionHint').textContent = collisionHints.box;
$('maxTris').addEventListener('input', () => { syncChips(); updateTransform(); scheduleAnalyze(600); });
$('trisChips').addEventListener('click', (e) => {
  const b = e.target.closest('button');
  if (!b) return;
  $('maxTris').value = b.dataset.v;
  syncChips();
  updateTransform();
  scheduleAnalyze(0);
});
function syncChips() {
  for (const b of $('trisChips').children) b.classList.toggle('active', b.dataset.v === $('maxTris').value);
}
$('name').addEventListener('input', () => { state.nameTouched = true; validateName(); });
$('showPed').addEventListener('change', (e) => { ped.visible = e.target.checked; });
$('wireframe').addEventListener('change', applyWireframe);
$('frame').addEventListener('click', frameAll);
$('convert').addEventListener('click', convert);
$('closeResult').addEventListener('click', () => ($('result').hidden = true));
$('browse').addEventListener('click', async () => {
  const r = await api('/api/browse', {});
  if (r.path) $('outDir').value = r.path;
});
addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && (e.ctrlKey || e.metaKey) && !$('convert').disabled) convert();
});

// ------------------------------------------------------------------ démarrage

syncChips();
setCollisionTris(2000);
initCleanup({
  scene, camera, canvas, modelRoot, orbit: controls,
  getModel: () => state.model,
  fileName: () => state.file?.name,
  setView: (v) => setView(v),
  hasLights: () => lightCount() > 0,
  reload: (file) => loadFile(file, state.height / Math.max(1e-6, state.rawSize.y)),
  onGeometryChanged: () => {
    // Triangles restants + taille, et rappel que l'aperçu GTA / la conversion attendent le nettoyage appliqué.
    let tris = 0;
    state.model?.traverse((o) => { if (o.isMesh && o.visible) tris += (o.geometry.index ? o.geometry.index.count : o.geometry.attributes.position.count) / 3; });
    state.triangles = Math.round(tris);
    updateTransform();
    const dirty = isCleanupDirty();
    $('convert').disabled = dirty || !state.uploadId;
    $('convert').textContent = dirty ? t('Applique le nettoyage d\'abord', 'Apply the cleanup first') : t('Convertir', 'Convert');
  },
});
initLights({
  scene, camera, canvas, modelRoot, orbit: controls,
  pickTargets: () => (state.view === 'original' ? [modelRoot] : [lodGroup]),
  env: { hemi, sun, fill },
});
frameAll();
// Page GitHub : ouverte dans le navigateur habituel (pas dans la fenêtre de l'application).
$('githubLink').addEventListener('click', () => api('/api/github', {}).catch(() => window.open(state.github, '_blank')));

// Langue : textes fixes de la page, puis bouton FR / EN (recharge la page).
translatePage();
for (const b of $('langSwitch').querySelectorAll('button')) {
  b.classList.toggle('active', b.dataset.lang === lang);
  b.addEventListener('click', () => {
    if (b.dataset.lang === lang) return;
    if (state.model && !confirm(t('Changer de langue recharge la page : le modèle ouvert sera fermé. Continuer ?', 'Changing the language reloads the page: the open model will be closed. Continue?'))) return;
    setLang(b.dataset.lang);
  });
}

// Battement : tant que la fenêtre est ouverte, l'application reste lancée.
const ping = () => fetch('/api/ping', { headers: { 'X-Token': token } }).catch(() => {});
ping();
setInterval(ping, 10000);
addEventListener('pagehide', () => navigator.sendBeacon(`/api/bye?t=${token}`));

api('/api/info').then((info) => {
  if (info.error) return showResult(`<h3 class="err">${esc(info.error)}</h3>`);
  $('version').textContent = info.version ? `v${info.version}` : '';
  state.github = info.github;
  $('outDir').value = info.defaultOutput; // dernier dossier utilisé (retenu par l'application)
  $('browse').hidden = !info.canBrowse;
});
