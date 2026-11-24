// Onglet « Nettoyer » : sélectionner et supprimer des parties du modèle (pièce, îlot, zone),
// puis réexporter un GLB propre qui remplace le modèle chargé.
import * as THREE from 'three';
import { GLTFExporter } from 'three/addons/exporters/GLTFExporter.js';
import { t as tr, num } from './i18n.js';

const $ = (id) => document.getElementById(id);
const fmtInt = (n) => num(n);

let ctx = null;          // { scene, camera, canvas, orbit, modelRoot, getModel(), setView(), onGeometryChanged(), reload(file), hasLights() }
let active = false;      // onglet ouvert
let tool = 'island';     // 'piece' | 'island' | 'box'
let selection = new Map(); // Mesh -> Set<index de triangle>
let undoStack = [];      // [{ changes: [{ mesh, index, visible }] }]
let initialTris = 0;
let overlay = null;
let islandCache = new WeakMap(); // Mesh -> { version, tri2island: Int32Array }
let dirty = false;

export function initCleanup(context) {
  ctx = context;
  overlay = new THREE.Mesh(new THREE.BufferGeometry(), new THREE.MeshBasicMaterial({
    color: 0xff7a1a, transparent: true, opacity: 0.45, depthTest: false, side: THREE.DoubleSide,
  }));
  overlay.renderOrder = 20;
  overlay.frustumCulled = false;
  ctx.scene.add(overlay);
  bindUi();
  document.addEventListener('tabchange', (e) => setActive(e.detail === 'cleanup'));
}

/** Nouveau modèle : on repart de zéro. */
export function resetCleanup() {
  selection = new Map();
  undoStack = [];
  islandCache = new WeakMap();
  dirty = false;
  initialTris = countTriangles();
  updateOverlay();
  renderPanel();
}

export function isCleanupDirty() { return dirty; }

// ------------------------------------------------------------------ géométrie

function meshes() {
  const out = [];
  ctx.getModel()?.traverse((o) => { if (o.isMesh && o.visible) out.push(o); });
  return out;
}

/** Garantit un index (triangles numérotés) sur la géométrie. */
function ensureIndex(mesh) {
  const g = mesh.geometry;
  if (!g.index) {
    const n = g.attributes.position.count;
    const idx = new Uint32Array(n);
    for (let i = 0; i < n; i++) idx[i] = i;
    g.setIndex(new THREE.BufferAttribute(idx, 1));
  }
  return g.index;
}

function triCount(mesh) { return ensureIndex(mesh).count / 3; }
function countTriangles() { return meshes().reduce((s, m) => s + triCount(m), 0); }

/** Boîte englobante calculée sur les sommets réellement utilisés (sinon les parties supprimées compteraient encore). */
function refreshBounds(mesh) {
  const g = mesh.geometry, idx = g.index.array, pos = g.attributes.position;
  const box = new THREE.Box3();
  const v = new THREE.Vector3();
  for (let i = 0; i < idx.length; i++) box.expandByPoint(v.fromBufferAttribute(pos, idx[i]));
  g.boundingBox = box;
  g.boundingSphere = box.isEmpty() ? new THREE.Sphere() : box.getBoundingSphere(new THREE.Sphere());
}

/** Numéro d'îlot (morceau connecté) pour chaque triangle, sommets soudés par position. */
function islandsOf(mesh) {
  const g = mesh.geometry;
  const version = g.index.version + ':' + g.index.count;
  const cached = islandCache.get(mesh);
  if (cached && cached.version === version) return cached.tri2island;
  const pos = g.attributes.position, idx = g.index.array;
  // Soudure des sommets identiques (coutures d'UV) au 1/10 de mm.
  const box = g.boundingBox ?? new THREE.Box3().setFromBufferAttribute(pos);
  const q = 1e4 / Math.max(1, box.getSize(new THREE.Vector3()).length());
  const weld = new Int32Array(pos.count);
  const keys = new Map();
  for (let i = 0; i < pos.count; i++) {
    const k = `${Math.round(pos.getX(i) * q)},${Math.round(pos.getY(i) * q)},${Math.round(pos.getZ(i) * q)}`;
    let id = keys.get(k);
    if (id === undefined) { id = keys.size; keys.set(k, id); }
    weld[i] = id;
  }
  const parent = new Int32Array(keys.size);
  for (let i = 0; i < parent.length; i++) parent[i] = i;
  const find = (x) => { while (parent[x] !== x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
  const union = (a, b) => { a = find(a); b = find(b); if (a !== b) parent[a] = b; };
  for (let t = 0; t < idx.length; t += 3) {
    union(weld[idx[t]], weld[idx[t + 1]]);
    union(weld[idx[t]], weld[idx[t + 2]]);
  }
  const tri2island = new Int32Array(idx.length / 3);
  for (let t = 0; t < tri2island.length; t++) tri2island[t] = find(weld[idx[t * 3]]);
  islandCache.set(mesh, { version, tri2island });
  return tri2island;
}

// ------------------------------------------------------------------ sélection

function addTris(mesh, tris, mode) {
  let set = selection.get(mesh);
  if (!set) { set = new Set(); selection.set(mesh, set); }
  for (const t of tris) { if (mode === 'remove') set.delete(t); else set.add(t); }
  if (!set.size) selection.delete(mesh);
}

function selectedCount() { let n = 0; for (const s of selection.values()) n += s.size; return n; }

function clickSelect(ev) {
  const ray = rayFrom(ev);
  const hit = ray.intersectObjects(meshes(), false)[0];
  const mode = ev.ctrlKey || ev.metaKey ? 'remove' : ev.shiftKey ? 'add' : 'replace';
  if (mode === 'replace') selection = new Map();
  if (hit && hit.faceIndex !== undefined) {
    const mesh = hit.object;
    const n = triCount(mesh);
    let tris;
    if (tool === 'piece') {
      tris = Array.from({ length: n }, (_, i) => i);
    } else {
      const isl = islandsOf(mesh);
      const target = isl[hit.faceIndex];
      tris = [];
      for (let t = 0; t < isl.length; t++) if (isl[t] === target) tris.push(t);
    }
    addTris(mesh, tris, mode);
  }
  updateOverlay();
  renderPanel();
}

function boxSelect(r0, r1, ev) {
  const rect = ctx.canvas.getBoundingClientRect();
  const toNdc = (p) => [((p.x - rect.left) / rect.width) * 2 - 1, -((p.y - rect.top) / rect.height) * 2 + 1];
  const [ax, ay] = toNdc(r0), [bx, by] = toNdc(r1);
  const minX = Math.min(ax, bx), maxX = Math.max(ax, bx), minY = Math.min(ay, by), maxY = Math.max(ay, by);
  const mode = ev.ctrlKey || ev.metaKey ? 'remove' : ev.shiftKey ? 'add' : 'replace';
  if (mode === 'replace') selection = new Map();
  const v = new THREE.Vector3(), c = new THREE.Vector3();
  for (const mesh of meshes()) {
    const g = mesh.geometry, idx = ensureIndex(mesh).array, pos = g.attributes.position;
    mesh.updateMatrixWorld();
    const tris = [];
    for (let t = 0; t < idx.length / 3; t++) {
      // Centre du triangle projeté à l'écran.
      c.set(0, 0, 0);
      for (let k = 0; k < 3; k++) c.add(v.fromBufferAttribute(pos, idx[t * 3 + k]));
      c.multiplyScalar(1 / 3).applyMatrix4(mesh.matrixWorld).project(ctx.camera);
      if (c.z < 1 && c.x >= minX && c.x <= maxX && c.y >= minY && c.y <= maxY) tris.push(t);
    }
    if (tris.length) addTris(mesh, tris, mode);
  }
  updateOverlay();
  renderPanel();
}

function updateOverlay() {
  const pts = [];
  const v = new THREE.Vector3();
  for (const [mesh, set] of selection) {
    const idx = mesh.geometry.index.array, pos = mesh.geometry.attributes.position;
    mesh.updateMatrixWorld();
    for (const t of set) for (let k = 0; k < 3; k++) {
      v.fromBufferAttribute(pos, idx[t * 3 + k]).applyMatrix4(mesh.matrixWorld);
      pts.push(v.x, v.y, v.z);
    }
  }
  overlay.geometry.dispose();
  overlay.geometry = new THREE.BufferGeometry();
  overlay.geometry.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
  overlay.visible = active && pts.length > 0;
}

// ------------------------------------------------------------------ actions

/** Supprime des triangles (par maillage), avec sauvegarde pour annuler. */
function removeTriangles(toRemove) {
  const changes = [];
  for (const [mesh, set] of toRemove) {
    if (!set.size) continue;
    const g = mesh.geometry, old = g.index.array;
    changes.push({ mesh, index: old.slice(), visible: mesh.visible });
    const keep = [];
    for (let t = 0; t < old.length / 3; t++) if (!set.has(t)) keep.push(old[t * 3], old[t * 3 + 1], old[t * 3 + 2]);
    g.setIndex(new THREE.BufferAttribute(new Uint32Array(keep), 1));
    if (!keep.length) mesh.visible = false;
    refreshBounds(mesh);
  }
  if (!changes.length) return;
  undoStack.push({ changes });
  selection = new Map();
  dirty = true;
  updateOverlay();
  ctx.onGeometryChanged();
  renderPanel();
}

function deleteSelection() { removeTriangles(selection); }

function keepSelection() {
  if (!selection.size) return;
  const toRemove = new Map();
  for (const mesh of meshes()) {
    const sel = selection.get(mesh) ?? new Set();
    const set = new Set();
    for (let t = 0; t < triCount(mesh); t++) if (!sel.has(t)) set.add(t);
    toRemove.set(mesh, set);
  }
  removeTriangles(toRemove);
}

function undo() {
  const op = undoStack.pop();
  if (!op) return;
  for (const c of op.changes) {
    c.mesh.geometry.setIndex(new THREE.BufferAttribute(c.index, 1));
    c.mesh.visible = c.visible;
    refreshBounds(c.mesh);
  }
  dirty = undoStack.length > 0;
  selection = new Map();
  updateOverlay();
  ctx.onGeometryChanged();
  renderPanel();
}

/** Retire les sommets inutilisés : sinon ils resteraient dans le GLB exporté. */
function compact(mesh) {
  const g = mesh.geometry, idx = g.index.array;
  const remap = new Int32Array(g.attributes.position.count).fill(-1);
  let n = 0;
  const newIdx = new Uint32Array(idx.length);
  for (let i = 0; i < idx.length; i++) {
    if (remap[idx[i]] < 0) remap[idx[i]] = n++;
    newIdx[i] = remap[idx[i]];
  }
  const out = new THREE.BufferGeometry();
  for (const [name, attr] of Object.entries(g.attributes)) {
    const size = attr.itemSize;
    const arr = new attr.array.constructor(n * size);
    for (let old = 0; old < remap.length; old++) {
      const nw = remap[old];
      if (nw < 0) continue;
      for (let k = 0; k < size; k++) arr[nw * size + k] = attr.getComponent ? attr.getComponent(old, k) : attr.array[old * size + k];
    }
    out.setAttribute(name, new THREE.BufferAttribute(arr, size, attr.normalized));
  }
  out.setIndex(new THREE.BufferAttribute(newIdx, 1));
  out.groups = g.groups;
  return out;
}

async function apply() {
  if (!dirty) return;
  if (ctx.hasLights() && !confirm(tr('Appliquer le nettoyage recharge le modèle : les lumières placées seront perdues. Continuer ?', 'Applying the cleanup reloads the model: placed lights will be lost. Continue?'))) return;
  setBusy(true);
  try {
    const model = ctx.getModel();
    // Copie nettoyée : on ne touche pas à la scène affichée tant que l'export n'a pas réussi.
    const copy = model.clone(true);
    const originals = [];
    model.traverse((o) => { if (o.isMesh) originals.push(o); });
    const clones = [];
    copy.traverse((o) => { if (o.isMesh) clones.push(o); });
    clones.forEach((c, i) => {
      const o = originals[i];
      if (!o.visible || o.geometry.index.count === 0) { c.visible = false; return; }
      c.geometry = compact(o);
    });
    const glb = await new GLTFExporter().parseAsync(copy, { binary: true, onlyVisible: true, maxTextureSize: 8192 });
    const name = (ctx.fileName() || 'modele.glb').replace(/(_propre)?\.(glb|gltf)$/i, '') + '_propre.glb';
    await ctx.reload(new File([glb], name, { type: 'model/gltf-binary' }));
  } catch (e) {
    console.error(e);
    alert(`${tr('Export impossible', 'Export failed')} : ${e.message ?? e}`);
  } finally {
    setBusy(false);
  }
}

function setBusy(on) {
  $('cleanApply').disabled = on || !dirty;
  $('cleanApply').textContent = on ? tr('Export en cours…', 'Exporting…') : tr('Appliquer et recharger le modèle', 'Apply and reload the model');
}

// ------------------------------------------------------------------ interface

function setActive(on) {
  active = on;
  if (on) ctx.setView('original');
  ctx.orbit.mouseButtons.LEFT = on && tool === 'box' ? null : THREE.MOUSE.ROTATE;
  ctx.canvas.style.cursor = on ? (tool === 'box' ? 'crosshair' : 'pointer') : '';
  updateOverlay();
  renderPanel();
}

function setTool(t) {
  tool = t;
  for (const b of document.querySelectorAll('#cleanTools button')) b.classList.toggle('active', b.dataset.tool === t);
  setActive(active);
  $('cleanToolHint').textContent = {
    piece: tr('Clic : sélectionne une pièce entière du modèle (un objet du GLB).', 'Click: selects a whole part of the model (one GLB object).'),
    island: tr('Clic : sélectionne un morceau détaché (îlot), même s\'il fait partie du même objet.', 'Click: selects a detached piece (island), even inside the same object.'),
    box: tr('Glisser : sélectionne tout ce qui est dans le rectangle, y compris derrière. Clic droit pour tourner la vue.', 'Drag: selects everything inside the rectangle, including behind. Right-click to rotate the view.'),
  }[t];
}

function renderPanel() {
  if (!$('cleanStats')) return;
  const now = ctx.getModel() ? countTriangles() : 0;
  const sel = selectedCount();
  $('cleanStats').innerHTML = ctx.getModel()
    ? `Triangles${tr(' :', ':')} <b>${fmtInt(now)}</b>${now !== initialTris ? ` <span class="muted">(${tr('au départ', 'originally')} ${fmtInt(initialTris)})</span>` : ''}<br>${tr('Sélection :', 'Selection:')} <b>${fmtInt(sel)}</b> triangle(s)`
    : `<span class="muted">${tr('Charge un modèle.', 'Load a model.')}</span>`;
  $('cleanDelete').disabled = !sel;
  $('cleanKeep').disabled = !sel;
  $('cleanClear').disabled = !sel;
  $('cleanUndo').disabled = !undoStack.length;
  $('cleanApply').disabled = !dirty;
  $('cleanDirty').hidden = !dirty;
}

function rayFrom(ev) {
  const r = ctx.canvas.getBoundingClientRect();
  const ndc = new THREE.Vector2(((ev.clientX - r.left) / r.width) * 2 - 1, -((ev.clientY - r.top) / r.height) * 2 + 1);
  const ray = new THREE.Raycaster();
  ray.setFromCamera(ndc, ctx.camera);
  return ray;
}

function bindUi() {
  for (const b of document.querySelectorAll('#cleanTools button')) b.addEventListener('click', () => setTool(b.dataset.tool));
  $('cleanDelete').addEventListener('click', deleteSelection);
  $('cleanKeep').addEventListener('click', keepSelection);
  $('cleanClear').addEventListener('click', () => { selection = new Map(); updateOverlay(); renderPanel(); });
  $('cleanUndo').addEventListener('click', undo);
  $('cleanApply').addEventListener('click', apply);

  const box = $('selectBox');
  let down = null;
  ctx.canvas.addEventListener('pointerdown', (e) => {
    if (!active || e.button !== 0) return;
    down = { x: e.clientX, y: e.clientY };
  });
  ctx.canvas.addEventListener('pointermove', (e) => {
    if (!active || !down || tool !== 'box') return;
    const r = $('viewer').getBoundingClientRect();
    box.hidden = false;
    box.style.left = `${Math.min(down.x, e.clientX) - r.left}px`;
    box.style.top = `${Math.min(down.y, e.clientY) - r.top}px`;
    box.style.width = `${Math.abs(e.clientX - down.x)}px`;
    box.style.height = `${Math.abs(e.clientY - down.y)}px`;
  });
  addEventListener('pointerup', (e) => {
    if (!active || !down || e.button !== 0) { down = null; return; }
    const start = down;
    down = null;
    box.hidden = true;
    const moved = Math.hypot(e.clientX - start.x, e.clientY - start.y);
    if (tool === 'box' && moved > 4) boxSelect(start, { x: e.clientX, y: e.clientY }, e);
    else if (tool !== 'box' && moved <= 4 && e.target === ctx.canvas) clickSelect(e);
  });
  addEventListener('keydown', (e) => {
    if (!active) return;
    const typing = ['INPUT', 'SELECT', 'TEXTAREA'].includes(document.activeElement?.tagName);
    if (typing) return;
    if (e.key === 'Delete') { deleteSelection(); e.preventDefault(); }
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') { undo(); e.preventDefault(); }
    if (e.key === 'Escape') { selection = new Map(); updateOverlay(); renderPanel(); }
    if (e.key === '1') setTool('piece');
    if (e.key === '2') setTool('island');
    if (e.key === '3') setTool('box');
  });
  setTool('island');
}
