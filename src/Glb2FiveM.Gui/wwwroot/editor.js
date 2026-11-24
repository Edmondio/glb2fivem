// Éditeur d'image intégré : miniPaint (MIT), chargé dans une iframe de la même origine.
// On lui passe la texture courante, et on récupère le résultat aplati de tous ses calques.

import { t as tr } from './i18n.js';

const $ = (id) => document.getElementById(id);
let frame = null;     // iframe miniPaint (créée une seule fois, réutilisée)
let session = null;   // { onApply }

async function ensureFrame() {
  if (frame?.contentWindow?.Layers) return frame.contentWindow;
  if (!frame) {
    frame = document.createElement('iframe');
    frame.title = tr('Éditeur d\'image', 'Image editor');
    frame.src = '/vendor/minipaint/index.html';
    $('editorFrameHost').appendChild(frame);
  }
  for (let i = 0; i < 200 && !frame.contentWindow?.Layers; i++) await sleep(50);
  const w = frame.contentWindow;
  if (!w?.Layers) throw new Error(tr('miniPaint introuvable (relance tools\\setup-deps pour récupérer les sources).', 'miniPaint not found (run tools\\setup-deps again to fetch the sources).'));
  return w;
}

/**
 * Ouvre une image dans l'éditeur.
 * @param {{ source: CanvasImageSource, title: string, onApply: (canvas: HTMLCanvasElement) => void }} opts
 */
export async function openImageEditor({ source, title, onApply }) {
  $('editorTitle').textContent = title;
  $('editorModal').hidden = false;
  $('editorLoading').hidden = false;
  session = { onApply };
  try {
    const w = await ensureFrame();
    const c = document.createElement('canvas');
    c.width = source.width;
    c.height = source.height;
    c.getContext('2d').drawImage(source, 0, 0);

    // Chemin "Fichier > Ouvrir" de miniPaint : remplace le calque et met le canevas à la taille de l'image.
    await w.Layers.reset_layers(false);
    w.FileOpen.file_open_data_url_handler(c.toDataURL('image/png'));
    for (let i = 0; i < 200; i++) {
      const l = w.AppConfig.layers;
      if (l.length && l[l.length - 1].type === 'image' && w.AppConfig.WIDTH === c.width && w.AppConfig.HEIGHT === c.height) break;
      await sleep(50);
    }
    // Zoom "ajuster à la fenêtre".
    await sleep(100);
    [...w.document.querySelectorAll('button')].find((b) => b.textContent.trim() === 'Fit')?.click();
  } catch (e) {
    alert(e.message ?? e);
    close();
  } finally {
    $('editorLoading').hidden = true;
  }
}

function apply() {
  const w = frame?.contentWindow;
  if (!w?.Layers || !session) return close();
  // Tous les calques aplatis, à la taille actuelle du canevas (recadrage / redimensionnement respectés).
  const c = document.createElement('canvas');
  c.width = w.AppConfig.WIDTH;
  c.height = w.AppConfig.HEIGHT;
  w.Layers.convert_layers_to_canvas(c.getContext('2d'));
  const cb = session.onApply;
  close();
  cb(c);
}

function close() {
  $('editorModal').hidden = true;
  session = null;
}

function sleep(ms) { return new Promise((r) => setTimeout(r, ms)); }

$('editorApply').addEventListener('click', apply);
$('editorCancel').addEventListener('click', close);
