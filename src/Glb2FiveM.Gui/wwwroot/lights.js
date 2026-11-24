// Onglet « Lumières » : clic sur le modèle pour poser une lumière, réglages, aperçu animé.
// Les lumières sont exportées dans le .ydr (coordonnées GTA : three (x, y, z) -> GTA (x, -z, y)).
import * as THREE from 'three';
import { TransformControls } from 'three/addons/controls/TransformControls.js';
import { t as tr, num, translatePage } from './i18n.js';

const $ = (id) => document.getElementById(id);
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

export const EFFECTS = [
  [0, tr('Constante', 'Steady')],
  [15, tr('Bougie', 'Candle')],
  [17, tr('Feu', 'Fire')],
  [19, tr('Électrique (néon qui grésille)', 'Electric (buzzing neon)')],
  [3, tr('Clignote 1 fois / s', 'Blinks 1× / s')],
  [4, tr('Clignote 2 fois / s', 'Blinks 2× / s')],
  [5, tr('Clignote 5 fois / s', 'Blinks 5× / s')],
  [9, tr('Alarme', 'Alarm')],
  [20, tr('Stroboscope', 'Strobe')],
  [14, 'Disco'],
  [1, tr('Aléatoire', 'Random')],
  [10, tr('Seulement sous la pluie', 'Only when raining')],
];
const COLOR_PRESETS = ['#ffd9a0', '#ffffff', '#cfe0ff', '#ff9a3c', '#ff3b30', '#2f7bff', '#3dff7a', '#ff4fd8'];
const TIME_ALWAYS = 0xFFFFFF;
const TIME_NIGHT = 0xF0003F; // 20h -> 6h
const PREVIEW_SCALE = 6;    // intensité GTA -> candela three.js (réglé à l'œil)

let ctx = null;             // { scene, camera, canvas, orbit, modelRoot, pickTargets(), env: { hemi, sun, fill } }
let gizmo = null;           // poignée de déplacement / rotation (TransformControls)
let proxy = null;           // objet manipulé par la poignée
let ghost = null;           // aperçu sous la souris pendant la pose
let gizmoMode = 'translate';
let lights = [];
let selected = null;
let placing = null;         // null | { move: light }
let nextId = 1;
let coronaTex = null;

/** À appeler une fois au démarrage. */
export function initLights(context) {
  ctx = context;
  coronaTex = makeCoronaTexture();
  setupGizmo();
  bindUi();
  render();
}

export function lightCount() { return lights.length; }

/** Supprime toutes les lumières (nouveau modèle chargé). */
export function clearLights() {
  gizmo?.detach();
  for (const l of lights) disposeLight(l);
  lights = [];
  selected = null;
  stopPlacing();
  render();
}

/** Lumières au format attendu par le serveur (coordonnées GTA finales du prop). */
export function lightsPayload() {
  return lights.map((l) => {
    const { pos, dir } = worldPose(l);
    return {
      type: l.type === 'tube' ? 'capsule' : l.type,
      length: l.length,
      position: [pos.x, -pos.z, pos.y],
      direction: [dir.x, -dir.z, dir.y],
      color: hexToRgb(l.color),
      intensity: l.intensity,
      falloff: l.falloff,
      coneAngle: l.coneAngle,
      coronaSize: l.coronaSize,
      timeFlags: l.night ? TIME_NIGHT : TIME_ALWAYS,
      flashiness: l.effect,
      shadows: l.shadows,
    };
  });
}

/** Recalcule la position des lumières quand le modèle change de taille / rotation / pivot. */
export function refreshLights() {
  for (const l of lights) place(l);
  syncProxy();
}

/** Animation des effets (appelée à chaque image). */
export function tickLights(t) {
  for (const l of lights) {
    const f = effectFactor(l.effect, t, l.id);
    const k = l.type === 'spot' ? 1.6 : 1;
    for (const li of l.obj.lights) li.intensity = (l.intensity * PREVIEW_SCALE * k * f) / l.obj.lights.length;
    l.obj.corona.material.opacity = 0.9 * f;
    l.obj.bulb.material.color.set(l.color).multiplyScalar(0.25 + 0.75 * f);
    if (l.effect === 14) for (const li of l.obj.lights) li.color.setHSL((t * 0.25 + l.id * 0.17) % 1, 1, 0.55); // disco
  }
}

// ------------------------------------------------------------------ objets 3D

function createLight(hit) {
  const l = {
    id: nextId++,
    type: 'point',
    color: '#ffd9a0',
    intensity: 6,
    falloff: 6,
    coneAngle: 40,
    length: 1,
    coronaSize: 0.4,
    night: false,
    effect: 0,
    shadows: false,
    offset: 0.05,
    aim: 'surface',            // 'surface' | 'down' | 'custom'
    dirLocal: new THREE.Vector3(0, -1, 0),
    anchorLocal: new THREE.Vector3(),
    normalLocal: new THREE.Vector3(0, 1, 0),
    obj: null,
  };
  setAnchor(l, hit);
  buildObject(l);
  return l;
}

function setAnchor(l, hit) {
  const inv = ctx.modelRoot.matrixWorld.clone().invert();
  l.anchorLocal.copy(hit.point).applyMatrix4(inv);
  l.normalLocal.copy(hit.normal).transformDirection(inv);
}

function buildObject(l) {
  if (l.obj) disposeLight(l);
  const group = new THREE.Group();
  const color = new THREE.Color(l.color);
  const lights = [];
  if (l.type === 'spot') {
    const sp = new THREE.SpotLight(color, 1, l.falloff, THREE.MathUtils.degToRad(l.coneAngle), 0.4, 2);
    group.add(sp.target);
    lights.push(sp);
  } else if (l.type === 'tube') {
    // Pas de lumière "tube" dans three.js : 3 lumières ponctuelles réparties sur la longueur.
    for (let i = 0; i < 3; i++) lights.push(new THREE.PointLight(color, 1, l.falloff, 2));
  } else {
    lights.push(new THREE.PointLight(color, 1, l.falloff, 2));
  }
  // Ampoule (sphère, ou cylindre pour un tube) de rayon 1, mise à l'échelle selon la taille de la lumière.
  const bulbGeo = l.type === 'tube' ? new THREE.CylinderGeometry(1, 1, 1, 16, 1) : new THREE.SphereGeometry(1, 16, 12);
  const bulb = new THREE.Mesh(bulbGeo, new THREE.MeshBasicMaterial({ color }));
  const corona = new THREE.Sprite(new THREE.SpriteMaterial({
    map: coronaTex, color, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
  }));
  const ring = new THREE.Mesh(new THREE.SphereGeometry(1, 16, 12), new THREE.MeshBasicMaterial({ color: 0xffb84d, wireframe: true }));
  ring.visible = false;
  // Zone de clic invisible, assez grosse pour sélectionner même une toute petite lumière.
  const hit = new THREE.Mesh(new THREE.SphereGeometry(1, 8, 6), new THREE.MeshBasicMaterial({ visible: false }));
  for (const o of [bulb, corona, ring, hit]) o.userData.lightId = l.id;
  group.add(...lights, bulb, corona, ring, hit);
  ctx.scene.add(group);
  l.obj = { group, lights, light: lights[0], bulb, corona, ring, hit, cone: null };
  if (l.type === 'spot') {
    l.obj.cone = new THREE.LineSegments(new THREE.EdgesGeometry(new THREE.ConeGeometry(1, 1, 24, 1, true)), new THREE.LineBasicMaterial({ color: 0xffb84d, transparent: true, opacity: 0.5 }));
    group.add(l.obj.cone);
  }
  place(l);
}

function disposeLight(l) {
  if (!l.obj) return;
  ctx.scene.remove(l.obj.group);
  l.obj.group.traverse((o) => { o.geometry?.dispose?.(); o.material?.dispose?.(); });
  for (const li of l.obj.lights) li.dispose?.();
  l.obj = null;
}

function worldPose(l) {
  const m = ctx.modelRoot.matrixWorld;
  const n = l.normalLocal.clone().transformDirection(m);
  const pos = l.anchorLocal.clone().applyMatrix4(m).addScaledVector(n, l.offset);
  const dir = l.aim === 'down' ? new THREE.Vector3(0, -1, 0)
    : l.aim === 'custom' ? l.dirLocal.clone().transformDirection(m)
    : n;
  return { pos, dir, n };
}

/** Rayon visuel de l'ampoule, proportionnel à la taille du halo (petites LED possibles). */
function bulbRadius(l) { return THREE.MathUtils.clamp(l.coronaSize * 0.12, 0.004, 0.04); }

function place(l) {
  if (!l.obj) return;
  const { pos, dir } = worldPose(l);
  const o = l.obj;
  const d = dir.clone().normalize();
  const r = bulbRadius(l);
  const q = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), d);
  if (l.type === 'tube') {
    o.lights.forEach((li, i) => li.position.copy(pos).addScaledVector(d, (i - 1) * l.length / 3));
    o.bulb.scale.set(r, l.length, r);
    o.bulb.quaternion.copy(q);
  } else {
    o.light.position.copy(pos);
    o.bulb.scale.setScalar(r);
  }
  for (const li of o.lights) { li.color.set(l.color); li.distance = l.falloff; }
  o.bulb.position.copy(pos);
  o.ring.position.copy(pos);
  // Sélection : ellipsoïde allongé le long du tube plutôt qu'une grosse sphère.
  const rr = Math.max(0.015, r * 2.2);
  if (l.type === 'tube') { o.ring.scale.set(rr, l.length / 2 + rr, rr); o.ring.quaternion.copy(q); }
  else { o.ring.scale.setScalar(rr); o.ring.quaternion.identity(); }
  o.ring.visible = selected === l;
  o.hit.position.copy(pos);
  o.hit.scale.setScalar(l.type === 'tube' ? Math.max(0.03, l.length / 2) : Math.max(0.03, r * 2));
  o.corona.position.copy(pos);
  o.corona.material.color.set(l.color);
  const cs = Math.max(0.01, l.coronaSize * 0.7) * (l.type === 'tube' ? Math.max(1, l.length) : 1);
  o.corona.scale.set(cs, cs, cs);
  if (l.type === 'spot') {
    o.light.angle = THREE.MathUtils.degToRad(l.coneAngle);
    o.light.target.position.copy(pos).add(d);
    // Cône indicatif (longueur 0,6 m) orienté selon la direction.
    const len = Math.min(0.6, l.falloff);
    const rr = Math.tan(THREE.MathUtils.degToRad(l.coneAngle)) * len;
    o.cone.scale.set(rr, len, rr);
    o.cone.position.copy(pos).addScaledVector(d, len / 2);
    o.cone.quaternion.setFromUnitVectors(new THREE.Vector3(0, -1, 0), d);
    o.cone.visible = selected === l;
  }
  if (selected === l && !gizmo?.dragging) syncProxy();
}

function makeCoronaTexture() {
  const c = document.createElement('canvas');
  c.width = c.height = 128;
  const g = c.getContext('2d');
  const grad = g.createRadialGradient(64, 64, 0, 64, 64, 64);
  grad.addColorStop(0, 'rgba(255,255,255,1)');
  grad.addColorStop(0.2, 'rgba(255,255,255,0.65)');
  grad.addColorStop(0.5, 'rgba(255,255,255,0.15)');
  grad.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 128, 128);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

/** Approximation visuelle des effets de GTA. */
function effectFactor(effect, t, seed) {
  const noise = (x) => (Math.sin(x * 12.9898 + seed * 78.233) * 43758.5453) % 1;
  const smoothNoise = (x) => {
    const i = Math.floor(x), f = x - i;
    const a = Math.abs(noise(i)), b = Math.abs(noise(i + 1));
    return a + (b - a) * (f * f * (3 - 2 * f));
  };
  switch (effect) {
    case 3: return (t % 1) < 0.5 ? 1 : 0.05;
    case 4: return ((t * 2) % 1) < 0.5 ? 1 : 0.05;
    case 5: return ((t * 5) % 1) < 0.5 ? 1 : 0.05;
    case 9: return 0.2 + 0.8 * (0.5 + 0.5 * Math.sin(t * Math.PI * 2));
    case 15: return 0.8 + 0.2 * smoothNoise(t * 8);
    case 17: return 0.6 + 0.4 * smoothNoise(t * 10);
    case 19: return smoothNoise(t * 3) > 0.85 ? (Math.abs(noise(Math.floor(t * 30))) > 0.5 ? 1 : 0.1) : 1;
    case 20: return ((t * 8) % 1) < 0.15 ? 1 : 0;
    case 1: return 0.3 + 0.7 * Math.abs(noise(Math.floor(t * 6)));
    case 14: return 1;
    default: return 1;
  }
}

// ------------------------------------------------------------------ pose / sélection

function startPlacing(moveLight = null) {
  placing = { move: moveLight };
  ctx.canvas.style.cursor = 'crosshair';
  $('placeHint').hidden = false;
  $('placeHint').textContent = moveLight
    ? tr('Clique sur le modèle pour déplacer la lumière · Échap pour annuler', 'Click the model to move the light · Esc to cancel')
    : tr('Clique sur le modèle pour poser la lumière · Échap pour annuler', 'Click the model to place the light · Esc to cancel');
  $('placeLight').classList.add('active');
}

function stopPlacing() {
  placing = null;
  if (ghost) ghost.visible = false;
  if (ctx) ctx.canvas.style.cursor = '';
  $('placeHint').hidden = true;
  $('placeLight').classList.remove('active');
}

function rayFrom(ev) {
  const r = ctx.canvas.getBoundingClientRect();
  const ndc = new THREE.Vector2(((ev.clientX - r.left) / r.width) * 2 - 1, -((ev.clientY - r.top) / r.height) * 2 + 1);
  const ray = new THREE.Raycaster();
  ray.setFromCamera(ndc, ctx.camera);
  return ray;
}

function surfaceHit(ray) {
  const hit = ray.intersectObjects(ctx.pickTargets(), true).find((h) => h.object.visible && h.face);
  if (!hit) return null;
  const normal = hit.face.normal.clone().transformDirection(hit.object.matrixWorld);
  if (normal.dot(ray.ray.direction) > 0) normal.negate(); // face orientée vers la caméra
  return { point: hit.point, normal };
}

function pick(ev) {
  if (gizmo.dragging || gizmo.axis) return; // clic sur la poignée
  const ray = rayFrom(ev);

  // 1. Clic sur une lumière existante : sélection.
  if (!placing) {
    const bulbs = lights.flatMap((l) => [l.obj.bulb, l.obj.hit]);
    const hitBulb = ray.intersectObjects(bulbs, false)[0];
    if (hitBulb) { select(lights.find((l) => l.id === hitBulb.object.userData.lightId)); return; }
    return;
  }

  // 2. Pose sur le modèle.
  const info = surfaceHit(ray);
  if (!info) return;

  if (placing.move) {
    setAnchor(placing.move, info);
    place(placing.move);
    select(placing.move);
  } else {
    const l = createLight(info);
    lights.push(l);
    select(l);
    if (lights.length === 1 && !$('nightMode').checked) { $('nightMode').checked = true; applyNight(); }
  }
  stopPlacing();
  render();
}

function select(l) {
  selected = l;
  for (const x of lights) place(x);
  attachGizmo();
  render();
  const card = document.querySelector(`[data-light="${l?.id}"]`);
  card?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
}

function removeLight(l) {
  if (selected === l) { gizmo.detach(); }
  disposeLight(l);
  lights = lights.filter((x) => x !== l);
  if (selected === l) selected = null;
  render();
}

// ------------------------------------------------------------------ poignée de précision

function setupGizmo() {
  proxy = new THREE.Object3D();
  ctx.scene.add(proxy);
  gizmo = new TransformControls(ctx.camera, ctx.canvas);
  gizmo.setSize(0.85);
  gizmo.setSpace('world');
  ctx.scene.add(gizmo.getHelper());
  gizmo.addEventListener('dragging-changed', (e) => { ctx.orbit.enabled = !e.value; if (!e.value) render(); });
  gizmo.addEventListener('objectChange', onGizmoChange);

  ghost = new THREE.Mesh(new THREE.SphereGeometry(0.045, 16, 12),
    new THREE.MeshBasicMaterial({ color: 0xffb84d, transparent: true, opacity: 0.7, depthTest: false }));
  ghost.renderOrder = 10;
  ghost.visible = false;
  ctx.scene.add(ghost);
}

function attachGizmo() {
  if (!selected) { gizmo.detach(); return; }
  if (gizmoMode === 'rotate' && selected.type === 'point') gizmoMode = 'translate';
  gizmo.setMode(gizmoMode);
  gizmo.setSpace(gizmoMode === 'rotate' ? 'local' : 'world');
  syncProxy();
  gizmo.attach(proxy);
}

/** Place l'objet manipulé sur la lumière sélectionnée (position + orientation du spot). */
function syncProxy() {
  if (!selected || !proxy) return;
  const { pos, dir } = worldPose(selected);
  proxy.position.copy(pos);
  proxy.quaternion.setFromUnitVectors(new THREE.Vector3(0, -1, 0), dir.clone().normalize());
  proxy.updateMatrixWorld();
}

function onGizmoChange() {
  const l = selected;
  if (!l) return;
  const inv = ctx.modelRoot.matrixWorld.clone().invert();
  if (gizmo.mode === 'translate') {
    const { n } = worldPose(l);
    // L'ancre reste "sous" la lumière : position moins le décalage le long de la normale.
    l.anchorLocal.copy(proxy.position).addScaledVector(n, -l.offset).applyMatrix4(inv);
  } else {
    const dir = new THREE.Vector3(0, -1, 0).applyQuaternion(proxy.quaternion);
    l.dirLocal.copy(dir).transformDirection(inv);
    l.aim = 'custom';
  }
  place(l);
  updateCoordInputs(l);
}

function updateCoordInputs(l) {
  const card = document.querySelector(`[data-light="${l.id}"]`);
  if (!card) return;
  const { pos } = worldPose(l);
  const gta = [pos.x, -pos.z, pos.y];
  card.querySelectorAll('input[data-coord]').forEach((el, i) => {
    if (document.activeElement !== el) el.value = gta[i].toFixed(3);
  });
  const aim = card.querySelector('select[data-set="aim"]');
  if (aim && aim.value !== l.aim) aim.value = l.aim;
}

/** Saisie directe des coordonnées GTA (mètres, repère du prop). */
function setCoords(l, gta) {
  const world = new THREE.Vector3(gta[0], gta[2], -gta[1]);
  const { n } = worldPose(l);
  const inv = ctx.modelRoot.matrixWorld.clone().invert();
  l.anchorLocal.copy(world).addScaledVector(n, -l.offset).applyMatrix4(inv);
  place(l);
  syncProxy();
}

function moveGhost(ev) {
  if (!placing) return;
  const hit = surfaceHit(rayFrom(ev));
  ghost.visible = !!hit;
  if (hit) ghost.position.copy(hit.point).addScaledVector(hit.normal, 0.05);
}

function applyNight() {
  const night = $('nightMode').checked;
  const e = ctx.env;
  e.hemi.intensity = night ? 0.06 : 1.6;
  e.sun.intensity = night ? 0.04 : 2.2;
  e.fill.intensity = night ? 0 : 0.6;
}

// ------------------------------------------------------------------ onglet

function render() {
  $('lightCount').textContent = lights.length ? `(${lights.length})` : '';
  const list = $('lightList');
  if (!lights.length) {
    list.innerHTML = '<p class="tex-empty muted">Aucune lumière. Clique sur « Placer une lumière » puis sur le modèle.</p>';
    translatePage(list);
    return;
  }
  list.innerHTML = lights.map((l, i) => card(l, i)).join('');
  translatePage(list);
  for (const l of lights) updateCoordInputs(l);
}

function card(l, i) {
  const open = selected === l;
  const effects = EFFECTS.map(([v, n]) => `<option value="${v}" ${v === l.effect ? 'selected' : ''}>${esc(n)}</option>`).join('');
  const presets = COLOR_PRESETS.map((c) => `<button class="swatch" style="background:${c}" data-set="color" data-value="${c}" title="${c}"></button>`).join('');
  return `
  <div class="light-card ${open ? 'open' : ''}" data-light="${l.id}">
    <div class="light-head" data-select="${l.id}">
      <span class="dot" style="background:${l.color}"></span>
      <b>${tr('Lumière', 'Light')} ${i + 1}</b>
      <button class="icon" data-delete="${l.id}" title="Supprimer">✕</button>
      <small class="muted">${{ spot: 'spot', tube: `tube ${fmt(l.length)} m`, point: tr('ponctuelle', 'point') }[l.type]} · ${fmt(l.intensity)} · ${fmt(l.falloff)} m${l.night ? tr(' · nuit', ' · night') : ''}${l.effect ? ' · ' + esc(EFFECTS.find((e) => e[0] === l.effect)?.[1] ?? '') : ''}</small>
    </div>
    <div class="light-body">
      <div class="seg full">
        <button data-set="type" data-value="point" class="${l.type === 'point' ? 'active' : ''}">Ponctuelle</button>
        <button data-set="type" data-value="spot" class="${l.type === 'spot' ? 'active' : ''}">Spot</button>
        <button data-set="type" data-value="tube" class="${l.type === 'tube' ? 'active' : ''}">Tube (néon)</button>
      </div>
      <div class="coords">
        <span class="muted">Position (m)</span>
        <label>X <input type="number" step="0.01" data-coord="0"></label>
        <label>Y <input type="number" step="0.01" data-coord="1"></label>
        <label>Z <input type="number" step="0.01" data-coord="2"></label>
      </div>
      <div class="seg full gizmo-modes">
        <button data-gizmo="translate" class="${gizmoMode === 'translate' || l.type === 'point' ? 'active' : ''}" title="Raccourci : G">✥ Déplacer</button>
        ${l.type !== 'point' ? `<button data-gizmo="rotate" class="${gizmoMode === 'rotate' ? 'active' : ''}" title="Raccourci : R">⟳ Orienter</button>` : ''}
      </div>
      <div class="color-row">
        <input type="color" data-set="color" value="${l.color}">
        <div class="swatches">${presets}</div>
      </div>
      ${slider('coronaSize', 'Taille', l.coronaSize, 0.01, 3, true, ' m', 0.01)}
      ${l.type === 'tube' ? slider('length', 'Longueur', l.length, 0.05, 5, true, ' m', 0.01) : ''}
      ${slider('intensity', 'Intensité', l.intensity, 0.05, 100, true, '', 0.01)}
      ${slider('falloff', 'Portée', l.falloff, 0.05, 50, true, ' m', 0.01)}
      ${l.type === 'spot' ? slider('coneAngle', 'Angle', l.coneAngle, 5, 90, false, '°') : ''}
      ${l.type !== 'point' ? `
        <label class="field compact"><span>${l.type === 'tube' ? 'Axe du tube' : 'Orientation'}</span>
          <select data-set="aim">
            <option value="surface" ${l.aim === 'surface' ? 'selected' : ''}>Perpendiculaire à la surface</option>
            <option value="down" ${l.aim === 'down' ? 'selected' : ''}>Vers le bas</option>
            <option value="custom" ${l.aim === 'custom' ? 'selected' : ''}>Personnalisée (poignée « Orienter »)</option>
          </select></label>` : ''}
      ${slider('offset', 'Décalage', l.offset * 100, 0, 50, false, ' cm', 1)}
      <label class="field compact"><span>Allumage</span>
        <select data-set="night">
          <option value="0" ${!l.night ? 'selected' : ''}>Toujours</option>
          <option value="1" ${l.night ? 'selected' : ''}>La nuit seulement (20h → 6h)</option>
        </select></label>
      <label class="field compact"><span>Effet</span><select data-set="effect">${effects}</select></label>
      <label class="check"><input type="checkbox" data-set="shadows" ${l.shadows ? 'checked' : ''}> Projeter des ombres <small class="muted">(plus coûteux)</small></label>
      <div class="img-actions">
        <button class="btn btn-small" data-move="${l.id}">Repositionner</button>
        <button class="btn btn-small" data-dup="${l.id}">Dupliquer</button>
      </div>
    </div>
  </div>`;
}

function slider(key, label, value, min, max, log, unit, step = 0.1) {
  const toPos = (v) => log ? (Math.log10(v) - Math.log10(min)) / (Math.log10(max) - Math.log10(min)) * 1000 : (v - min) / (max - min) * 1000;
  return `
    <label class="slider">${label}
      <input type="range" min="0" max="1000" value="${Math.round(toPos(value))}" data-slider="${key}" data-min="${min}" data-max="${max}" data-log="${log ? 1 : 0}" data-step="${step}">
      <output data-out="${key}">${fmt(value)}${unit}</output>
    </label>`;
}

function sliderValue(el) {
  const t = el.value / 1000, min = Number(el.dataset.min), max = Number(el.dataset.max), step = Number(el.dataset.step);
  const v = el.dataset.log === '1' ? 10 ** (Math.log10(min) + t * (Math.log10(max) - Math.log10(min))) : min + t * (max - min);
  return Math.round(v / step) * step;
}

function fmt(v) { return num(v, { maximumFractionDigits: v < 10 ? 2 : 1 }); }
function hexToRgb(h) { const n = parseInt(h.slice(1), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; }

function bindUi() {
  $('placeLight').addEventListener('click', () => (placing ? stopPlacing() : startPlacing()));
  $('nightMode').addEventListener('change', applyNight);
  $('lightSnap').addEventListener('change', (e) => {
    const v = Number(e.target.value);
    gizmo.setTranslationSnap(v > 0 ? v : null);
    gizmo.setRotationSnap(v > 0 ? THREE.MathUtils.degToRad(15) : null);
  });
  ctx.canvas.addEventListener('pointermove', moveGhost);

  // Clic court sur le canvas (pas un glisser de caméra).
  let down = null;
  ctx.canvas.addEventListener('pointerdown', (e) => { down = { x: e.clientX, y: e.clientY }; });
  ctx.canvas.addEventListener('pointerup', (e) => {
    if (!down || Math.hypot(e.clientX - down.x, e.clientY - down.y) > 5 || e.button !== 0) return;
    pick(e);
  });
  addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && placing) stopPlacing();
    const typing = ['INPUT', 'SELECT', 'TEXTAREA'].includes(document.activeElement?.tagName);
    if (!typing && selected && (e.key === 'g' || e.key === 'G')) { gizmoMode = 'translate'; attachGizmo(); render(); }
    if (!typing && selected && selected.type !== 'point' && (e.key === 'r' || e.key === 'R')) { gizmoMode = 'rotate'; attachGizmo(); render(); }
    if ((e.key === 'Delete' || e.key === 'Suppr') && selected && !typing) removeLight(selected);
  });

  const list = $('lightList');
  list.addEventListener('click', (e) => {
    const g = e.target.closest('button[data-gizmo]');
    if (g) { gizmoMode = g.dataset.gizmo; attachGizmo(); render(); return; }
    const t = e.target.closest('[data-delete],[data-move],[data-dup],[data-select],button[data-set]');
    if (!t) return;
    const id = Number(t.dataset.delete ?? t.dataset.move ?? t.dataset.dup ?? t.dataset.select ?? t.closest('[data-light]')?.dataset.light);
    const l = lights.find((x) => x.id === id);
    if (!l) return;
    if (t.dataset.delete) { e.stopPropagation(); removeLight(l); }
    else if (t.dataset.move) startPlacing(l);
    else if (t.dataset.dup) {
      const c = { ...l, id: nextId++, anchorLocal: l.anchorLocal.clone(), normalLocal: l.normalLocal.clone(), dirLocal: l.dirLocal.clone(), obj: null, offset: l.offset + 0.15 };
      buildObject(c);
      lights.push(c);
      select(c);
    } else if (t.dataset.select) select(selected === l ? null : l);
    else if (t.dataset.set) setProp(l, t.dataset.set, t.dataset.value, true);
  });
  list.addEventListener('input', (e) => {
    const el = e.target;
    const l = lights.find((x) => x.id === Number(el.closest('[data-light]')?.dataset.light));
    if (!l) return;
    if (el.dataset.slider) {
      const v = sliderValue(el);
      const key = el.dataset.slider;
      l[key] = key === 'offset' ? v / 100 : v;
      const unit = { falloff: ' m', coneAngle: '°', coronaSize: ' m', offset: ' cm', length: ' m' }[key] ?? '';
      el.closest('.slider').querySelector('output').textContent = fmt(v) + unit;
      place(l);
    } else if (el.dataset.set === 'color') {
      setProp(l, 'color', el.value, false);
    }
  });
  list.addEventListener('change', (e) => {
    const el = e.target;
    const l = lights.find((x) => x.id === Number(el.closest('[data-light]')?.dataset.light));
    if (!l) return;
    if (el.dataset.coord !== undefined) {
      const vals = [...el.closest('.coords').querySelectorAll('input[data-coord]')].map((x) => parseFloat(x.value.replace(',', '.')) || 0);
      setCoords(l, vals);
      return;
    }
    if (!el.dataset.set && !el.dataset.slider) return;
    if (el.dataset.set === 'shadows') setProp(l, 'shadows', el.checked, false);
    else if (el.tagName === 'SELECT') setProp(l, el.dataset.set, el.value, true);
    else if (el.dataset.slider) render();
  });
}

function setProp(l, key, value, rerender) {
  if (key === 'type') {
    l.type = value;
    if (value === 'tube' && l.aim === 'surface') {
      // Par défaut, le tube est couché le long de la surface (horizontal si possible).
      const { n } = worldPose(l);
      let axis = new THREE.Vector3().crossVectors(n, new THREE.Vector3(0, 1, 0));
      if (axis.lengthSq() < 1e-4) axis.set(1, 0, 0);
      l.dirLocal.copy(axis.normalize()).transformDirection(ctx.modelRoot.matrixWorld.clone().invert());
      l.aim = 'custom';
      if (l.coronaSize > 0.2) l.coronaSize = 0.1;
    }
    buildObject(l);
    attachGizmo();
  }
  else if (key === 'night') l.night = value === '1';
  else if (key === 'effect') l.effect = Number(value);
  else if (key === 'aim' && value === 'custom') {
    const { dir } = worldPose(l);
    l.dirLocal.copy(dir).transformDirection(ctx.modelRoot.matrixWorld.clone().invert());
    l.aim = 'custom';
  }
  else l[key] = value;
  place(l);
  if (rerender) render();
  else {
    const dot = document.querySelector(`[data-light="${l.id}"] .dot`);
    if (dot) dot.style.background = l.color;
  }
}
