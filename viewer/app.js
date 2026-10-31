'use strict';

// ---------------------------------------------------------------------------
// Host bridge. Inside the Windows app the page runs in WebView2 and talks to
// the host through chrome.webview; in a plain browser everything falls back to
// <input type=file>, drag & drop and the ?src= query parameter.
// ---------------------------------------------------------------------------
const host = window.chrome && window.chrome.webview ? window.chrome.webview : null;

const $ = (id) => document.getElementById(id);
const el = {
  viewport: $('viewport'),
  content: $('content'),
  empty: $('empty'),
  status: $('status'),
  fileName: $('file-name'),
  fileInput: $('file-input'),
  zoom: $('zoom'),
  zoomReset: $('btn-zoom-reset'),
  btnFind: $('btn-find'),
  findBar: $('find-bar'),
  findInput: $('find-input'),
};

// ---------------------------------------------------------------------------
// Lazy script loading: heavy libraries are pulled in only for their format.
// ---------------------------------------------------------------------------
const loaded = new Map();
function loadScript(src) {
  if (!loaded.has(src)) {
    loaded.set(src, new Promise((resolve, reject) => {
      const s = document.createElement('script');
      s.src = src;
      s.onload = resolve;
      s.onerror = () => { loaded.delete(src); reject(new Error('Could not load ' + src)); };
      document.head.appendChild(s);
    }));
  }
  return loaded.get(src);
}

function loadStyle(href, media) {
  if (document.querySelector(`link[href="${href}"]`)) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = href;
  if (media) l.media = media;
  document.head.appendChild(l);
}

// ---------------------------------------------------------------------------
// Source: a file to show, backed either by a URL or by a File/Blob.
// ---------------------------------------------------------------------------
class Source {
  constructor({ name, url, blob, ext }) {
    this.name = name;
    this.blob = blob || null;
    this.url = url || (blob ? URL.createObjectURL(blob) : null);
    this.ownsUrl = !url && !!blob;
    this.ext = ext || extOf(name);
    this._buffer = null;
  }
  async buffer() {
    if (!this._buffer) {
      if (this.blob) {
        this._buffer = await this.blob.arrayBuffer();
      } else {
        const r = await fetch(this.url);
        if (!r.ok) throw new Error(`Could not read the file (${r.status})`);
        this._buffer = await r.arrayBuffer();
      }
    }
    return this._buffer;
  }
  dispose() {
    if (this.ownsUrl) URL.revokeObjectURL(this.url);
  }
}

function extOf(name) {
  const m = /\.([^.\\/]+)$/.exec(name || '');
  return m ? m[1].toLowerCase() : '';
}

// ---------------------------------------------------------------------------
// Text decoding: UTF-8 / UTF-16 by BOM, otherwise UTF-8 with a Windows-1251
// fallback, which is what most legacy Cyrillic text files use.
// ---------------------------------------------------------------------------
function decodeText(buf) {
  const b = new Uint8Array(buf);
  if (b[0] === 0xFF && b[1] === 0xFE) return new TextDecoder('utf-16le').decode(b.subarray(2));
  if (b[0] === 0xFE && b[1] === 0xFF) return new TextDecoder('utf-16be').decode(b.subarray(2));
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(b);
  } catch {
    return new TextDecoder('windows-1251').decode(b);
  }
}

function looksBinary(buf) {
  const b = new Uint8Array(buf, 0, Math.min(buf.byteLength, 8192));
  if ((b[0] === 0xFF && b[1] === 0xFE) || (b[0] === 0xFE && b[1] === 0xFF)) return false;
  return b.includes(0);
}

function escapeHtml(s) {
  return s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

// ---------------------------------------------------------------------------
// Renderers. Each gets (source, container) and fills the container.
// `opts.fill` = the format scrolls itself (no app zoom, container fills view).
// `opts.find` = whether the app's find bar can search the content.
// ---------------------------------------------------------------------------
const renderers = [];
function register(exts, render, opts = {}) {
  renderers.push({ exts: new Set(exts), render, fill: !!opts.fill, find: opts.find !== false });
}

function pickRenderer(ext) {
  return renderers.find((r) => r.exts.has(ext)) || null;
}

// --- Text, optionally highlighted as `lang`. Also the fallback for any
// unknown extension that isn't binary.
async function renderText(src, box, lang) {
  const text = decodeText(await src.buffer());
  const pre = document.createElement('pre');
  pre.className = 'text';
  const code = document.createElement('code');
  pre.appendChild(code);
  // Highlighting is slow on huge files; show them plain.
  if (lang && text.length < 1_000_000) {
    await loadScript('lib/highlight.min.js');
    loadStyle('lib/hljs-light.css', '(prefers-color-scheme: light)');
    loadStyle('lib/hljs-dark.css', '(prefers-color-scheme: dark)');
    code.className = 'hljs';
    code.innerHTML = hljs.getLanguage(lang) ? hljs.highlight(text, { language: lang }).value : escapeHtml(text);
  } else {
    code.textContent = text;
  }
  box.appendChild(pre);
}

// ---------------------------------------------------------------------------
// Opening documents
// ---------------------------------------------------------------------------
let current = null;
let openSeq = 0;

function showMessage(title, detail, isError) {
  el.status.className = 'message' + (isError ? ' error' : '');
  el.status.innerHTML = `<div class="big">${escapeHtml(title)}</div>` + (detail ? `<div>${escapeHtml(detail)}</div>` : '');
  el.status.hidden = false;
}

async function openSource(src) {
  const seq = ++openSeq;
  if (current) current.dispose();
  current = src;

  document.title = `${src.name} — Visio`;
  el.fileName.textContent = src.name;
  el.empty.hidden = true;
  el.content.hidden = true;
  el.content.replaceChildren();
  el.content.className = '';
  el.viewport.scrollTop = 0;
  closeFind();
  showMessage('Opening…', src.name);

  let renderer = pickRenderer(src.ext);
  let render = renderer ? renderer.render : null;
  try {
    if (!renderer) {
      if (looksBinary(await src.buffer())) {
        throw new Error(`The .${src.ext || '?'} format is not supported`);
      }
      render = (s, box) => renderText(s, box);
    }
    const fill = !!(renderer && renderer.fill);
    el.content.classList.toggle('fill', fill);
    el.zoom.hidden = fill;
    el.btnFind.hidden = renderer ? !renderer.find : false;
    setZoom(1);

    // Each open renders into its own box, so a slow render that finishes after
    // the user opened another file writes into a detached node, not the new one.
    const box = document.createElement('div');
    box.className = 'doc';
    el.content.appendChild(box);
    await render(src, box);
    if (seq !== openSeq) return;
    el.status.hidden = true;
    el.content.hidden = false;
  } catch (err) {
    if (seq !== openSeq) return;
    console.error(err);
    el.content.replaceChildren();
    el.zoom.hidden = el.btnFind.hidden = true;
    showMessage('Could not open the file', err && err.message ? err.message : String(err), true);
  }
}

function openFile(file) {
  if (host) {
    // Let the host resolve the real path: it serves the file and can run converters.
    host.postMessageWithAdditionalObjects({ type: 'open-dropped' }, [file]);
  } else {
    openSource(new Source({ name: file.name, blob: file }));
  }
}

function openDialog() {
  if (host) host.postMessage({ type: 'pick' });
  else el.fileInput.click();
}

// ---------------------------------------------------------------------------
// Zoom (applies to formats rendered by the page itself, not PDF/media).
// ---------------------------------------------------------------------------
const ZOOM_STEPS = [0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4];
let zoom = 1;

function setZoom(z) {
  zoom = Math.min(4, Math.max(0.25, z));
  el.content.style.zoom = el.content.classList.contains('fill') ? '' : String(zoom);
  el.zoomReset.textContent = Math.round(zoom * 100) + '%';
}

function stepZoom(dir) {
  const next = dir > 0 ? ZOOM_STEPS.find((s) => s > zoom + 1e-6) : [...ZOOM_STEPS].reverse().find((s) => s < zoom - 1e-6);
  setZoom(next || zoom);
}

$('btn-zoom-in').onclick = () => stepZoom(1);
$('btn-zoom-out').onclick = () => stepZoom(-1);
el.zoomReset.onclick = () => setZoom(1);

el.viewport.addEventListener('wheel', (e) => {
  if (!e.ctrlKey) return;
  e.preventDefault();
  if (!el.zoom.hidden) stepZoom(e.deltaY < 0 ? 1 : -1);
}, { passive: false });

// ---------------------------------------------------------------------------
// Find (Chromium's window.find walks and highlights the selection for us).
// ---------------------------------------------------------------------------
function openFind() {
  if (el.btnFind.hidden || el.content.hidden) return;
  el.findBar.hidden = false;
  el.findInput.focus();
  el.findInput.select();
}

function closeFind() {
  el.findBar.hidden = true;
}

function findNext(backwards) {
  const q = el.findInput.value;
  if (!q) return;
  const found = window.find(q, false, backwards, true, false, false, false);
  el.findInput.style.outline = found ? '' : '2px solid #d33';
  // window.find moves focus to the match; keep typing in the box.
  el.findInput.focus();
}

el.btnFind.onclick = openFind;
$('find-next').onclick = () => findNext(false);
$('find-prev').onclick = () => findNext(true);
$('find-close').onclick = closeFind;
el.findInput.addEventListener('keydown', (e) => {
  if (e.key === 'Enter') { e.preventDefault(); findNext(e.shiftKey); }
  if (e.key === 'Escape') closeFind();
});
el.findInput.addEventListener('input', () => {
  el.findInput.style.outline = '';
  window.getSelection().removeAllRanges();
  findNext(false);
});

// ---------------------------------------------------------------------------
// Keyboard, drag & drop, file input
// ---------------------------------------------------------------------------
document.addEventListener('keydown', (e) => {
  if (!e.ctrlKey) {
    if (e.key === 'F3') { e.preventDefault(); findNext(e.shiftKey); }
    if (e.key === 'F11' && host) { e.preventDefault(); host.postMessage({ type: 'fullscreen' }); }
    if (e.key === 'Escape' && host && el.findBar.hidden) host.postMessage({ type: 'exit-fullscreen' });
    return;
  }
  const k = e.key.toLowerCase();
  if (k === 'o') { e.preventDefault(); openDialog(); }
  else if (k === 'f') { e.preventDefault(); openFind(); }
  else if (k === '=' || k === '+') { e.preventDefault(); stepZoom(1); }
  else if (k === '-') { e.preventDefault(); stepZoom(-1); }
  else if (k === '0') { e.preventDefault(); setZoom(1); }
});

$('btn-open').onclick = openDialog;
el.fileInput.onchange = () => {
  if (el.fileInput.files[0]) openFile(el.fileInput.files[0]);
  el.fileInput.value = '';
};

let dragDepth = 0;
document.addEventListener('dragenter', (e) => { e.preventDefault(); dragDepth++; el.viewport.classList.add('drag-over'); });
document.addEventListener('dragleave', () => { if (--dragDepth <= 0) { dragDepth = 0; el.viewport.classList.remove('drag-over'); } });
document.addEventListener('dragover', (e) => e.preventDefault());
document.addEventListener('drop', (e) => {
  e.preventDefault();
  dragDepth = 0;
  el.viewport.classList.remove('drag-over');
  const file = e.dataTransfer.files[0];
  if (file) openFile(file);
});

// ---------------------------------------------------------------------------
// Host messages
// ---------------------------------------------------------------------------
// Runs after formats.js has registered the renderers.
document.addEventListener('DOMContentLoaded', () => {
  if (host) {
    host.addEventListener('message', (e) => {
      const m = e.data || {};
      if (m.type === 'open') openSource(new Source({ name: m.name, url: m.url, ext: m.format }));
      else if (m.type === 'status') { el.empty.hidden = true; el.content.hidden = true; showMessage(m.title, m.detail); }
      else if (m.type === 'error') { el.empty.hidden = true; el.content.hidden = true; showMessage(m.title, m.detail, true); }
    });
    host.postMessage({ type: 'ready' });
  } else {
    // Browser testing: index.html?src=path/to/file.docx
    const src = new URLSearchParams(location.search).get('src');
    if (src) openSource(new Source({ name: decodeURIComponent(src.split('/').pop()), url: src }));
  }
});
