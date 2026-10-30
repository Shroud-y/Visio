'use strict';

// Format renderers. Each register() call maps file extensions to a function
// that fills the content box; see register() in app.js for the options.

// --- PDF: Chromium's built-in viewer (own zoom, search, print).
register(['pdf'], async (src, box) => {
  const frame = document.createElement('iframe');
  frame.src = src.url;
  frame.title = src.name;
  box.appendChild(frame);
}, { fill: true, find: false });

// --- Images. Click toggles fit-to-window / actual size.
register(['png', 'jpg', 'jpeg', 'jfif', 'gif', 'webp', 'avif', 'bmp', 'ico', 'svg'], (src, box) => new Promise((resolve, reject) => {
  const wrap = document.createElement('div');
  wrap.className = 'media';
  const img = new Image();
  img.alt = src.name;
  img.onload = () => resolve();
  img.onerror = () => reject(new Error('Зображення пошкоджене або формат не підтримується'));
  img.onclick = () => img.classList.toggle('actual');
  img.src = src.url;
  wrap.appendChild(img);
  box.appendChild(wrap);
}), { find: false });

// --- Video and audio.
register(['mp4', 'm4v', 'webm', 'ogv', 'mov', 'mp3', 'm4a', 'wav', 'ogg', 'oga', 'opus', 'flac', 'aac'], (src, box) => new Promise((resolve, reject) => {
  const isAudio = ['mp3', 'm4a', 'wav', 'ogg', 'oga', 'opus', 'flac', 'aac'].includes(src.ext);
  const wrap = document.createElement('div');
  wrap.className = 'media';
  const media = document.createElement(isAudio ? 'audio' : 'video');
  media.controls = true;
  media.onloadedmetadata = () => resolve();
  media.onerror = () => reject(new Error('Медіафайл пошкоджений або кодек не підтримується'));
  media.src = src.url;
  wrap.appendChild(media);
  box.appendChild(wrap);
}), { fill: true, find: false });

// --- Plain text (no highlighting).
register(['txt', 'text', 'log', 'nfo', 'out'], (src, box) => renderText(src, box, null));

// --- Source code and data files, highlighted by highlight.js.
const CODE_LANGS = {
  json: 'json', jsonc: 'json', geojson: 'json', ipynb: 'json',
  xml: 'xml', xsd: 'xml', xsl: 'xml', xslt: 'xml', plist: 'xml', csproj: 'xml', props: 'xml', targets: 'xml', config: 'xml', resx: 'xml', xaml: 'xml',
  html: 'xml', htm: 'xml', xhtml: 'xml', vue: 'xml',
  css: 'css', scss: 'scss', less: 'less',
  js: 'javascript', mjs: 'javascript', cjs: 'javascript', jsx: 'javascript',
  ts: 'typescript', tsx: 'typescript',
  py: 'python', pyw: 'python',
  cs: 'csharp', csx: 'csharp',
  c: 'c', h: 'c', cpp: 'cpp', cc: 'cpp', cxx: 'cpp', hpp: 'cpp', hh: 'cpp',
  java: 'java', kt: 'kotlin', kts: 'kotlin', go: 'go', rs: 'rust', swift: 'swift',
  php: 'php', rb: 'ruby', pl: 'perl', lua: 'lua', r: 'r', dart: 'dart', scala: 'scala',
  sql: 'sql', yaml: 'yaml', yml: 'yaml', toml: 'ini', ini: 'ini', cfg: 'ini', conf: 'ini', env: 'bash',
  sh: 'bash', bash: 'bash', zsh: 'bash', ps1: 'powershell', psm1: 'powershell', bat: 'dos', cmd: 'dos',
  diff: 'diff', patch: 'diff', dockerfile: 'dockerfile', makefile: 'makefile', mk: 'makefile',
  tex: 'latex', gradle: 'gradle', graphql: 'graphql', proto: 'protobuf', vb: 'vbnet',
};
register(Object.keys(CODE_LANGS), (src, box) => renderText(src, box, CODE_LANGS[src.ext]));

// --- Markdown. Raw HTML in the source is disabled so documents can't run script.
register(['md', 'markdown', 'mdown', 'mkd'], async (src, box) => {
  await Promise.all([loadScript('lib/markdown-it.umd.min.js'), loadScript('lib/highlight.min.js')]);
  loadStyle('lib/hljs-light.css', '(prefers-color-scheme: light)');
  loadStyle('lib/hljs-dark.css', '(prefers-color-scheme: dark)');
  const md = window.markdownit({
    html: false,
    linkify: true,
    highlight: (code, lang) => (lang && hljs.getLanguage(lang) ? hljs.highlight(code, { language: lang }).value : ''),
  });
  const article = document.createElement('article');
  article.className = 'markdown';
  article.innerHTML = md.render(decodeText(await src.buffer()));
  // Relative images/links resolve against the document, not the viewer.
  const base = new URL('.', new URL(src.url, location.href));
  if (!src.blob) {
    article.querySelectorAll('img[src]').forEach((i) => { i.src = new URL(i.getAttribute('src'), base).href; });
  }
  article.querySelectorAll('a[href]').forEach((a) => { a.target = '_blank'; a.rel = 'noopener'; });
  box.appendChild(article);
});

// --- Word (OOXML) via docx-preview: real pages, headers/footers, tables, images.
register(['docx', 'docm', 'dotx', 'dotm'], async (src, box) => {
  await loadScript('lib/jszip.min.js');
  await loadScript('lib/docx-preview.min.js');
  const style = document.createElement('div');
  const body = document.createElement('div');
  body.className = 'docx-host';
  box.append(style, body);
  await docx.renderAsync(await src.buffer(), body, style, {
    className: 'docx',
    inWrapper: true,
    breakPages: true,
    ignoreLastRenderedPageBreak: true,
    renderHeaders: true,
    renderFooters: true,
    renderFootnotes: true,
    renderEndnotes: true,
    experimental: true,
    useBase64URL: false,
  });
});

// --- Spreadsheets via SheetJS: xlsx/xls/xlsb/ods/csv. Sheet tabs at the bottom.
const MAX_SHEET_ROWS = 5000;
const MAX_SHEET_COLS = 200;

register(['xlsx', 'xlsm', 'xltx', 'xltm', 'xlsb', 'xls', 'xlt', 'ods', 'fods', 'numbers', 'csv', 'tsv'], async (src, box) => {
  await loadScript('lib/xlsx.full.min.js');
  const buf = await src.buffer();
  const isText = src.ext === 'csv' || src.ext === 'tsv';
  const wb = isText
    ? XLSX.read(decodeText(buf), { type: 'string', raw: true, FS: src.ext === 'tsv' ? '\t' : guessDelimiter(decodeText(buf)) })
    : XLSX.read(buf, { type: 'array', cellDates: true, sheetStubs: true });

  const sheetsBox = document.createElement('div');
  sheetsBox.className = 'sheet';
  const tabs = document.createElement('nav');
  tabs.className = 'sheet-tabs';
  box.append(sheetsBox, tabs);

  const show = (i) => {
    sheetsBox.replaceChildren(buildSheet(wb.Sheets[wb.SheetNames[i]]));
    [...tabs.children].forEach((t, j) => t.classList.toggle('active', i === j));
    el.viewport.scrollTo(0, 0);
  };
  wb.SheetNames.forEach((name, i) => {
    const b = document.createElement('button');
    b.textContent = name;
    b.onclick = () => show(i);
    tabs.appendChild(b);
  });
  tabs.hidden = wb.SheetNames.length < 2;
  show(0);
});

function guessDelimiter(text) {
  const line = text.slice(0, 2000).split(/\r?\n/)[0];
  const count = (ch) => line.split(ch).length - 1;
  return [',', ';', '\t', '|'].reduce((best, ch) => (count(ch) > count(best) ? ch : best), ',');
}

function colName(c) {
  let s = '';
  for (c++; c > 0; c = Math.floor((c - 1) / 26)) s = String.fromCharCode(65 + ((c - 1) % 26)) + s;
  return s;
}

function buildSheet(ws) {
  const frag = document.createDocumentFragment();
  if (!ws || !ws['!ref']) {
    const p = document.createElement('p');
    p.className = 'sheet-note';
    p.textContent = 'Аркуш порожній';
    frag.appendChild(p);
    return frag;
  }
  const range = XLSX.utils.decode_range(ws['!ref']);
  const lastRow = Math.min(range.e.r, range.s.r + MAX_SHEET_ROWS - 1);
  const lastCol = Math.min(range.e.c, range.s.c + MAX_SHEET_COLS - 1);

  // Merged cells: the top-left cell spans, the rest are skipped.
  const span = new Map();
  const covered = new Set();
  for (const m of ws['!merges'] || []) {
    span.set(m.s.r + ':' + m.s.c, { rows: m.e.r - m.s.r + 1, cols: m.e.c - m.s.c + 1 });
    for (let r = m.s.r; r <= m.e.r; r++) {
      for (let c = m.s.c; c <= m.e.c; c++) if (r !== m.s.r || c !== m.s.c) covered.add(r + ':' + c);
    }
  }

  const table = document.createElement('table');
  const colgroup = document.createElement('colgroup');
  colgroup.appendChild(document.createElement('col')).className = 'row-head';
  const cols = ws['!cols'] || [];
  for (let c = range.s.c; c <= lastCol; c++) {
    const col = document.createElement('col');
    const info = cols[c];
    if (info && info.hidden) col.style.visibility = 'collapse';
    else if (info && (info.wpx || info.wch)) col.style.width = (info.wpx || Math.round(info.wch * 7 + 5)) + 'px';
    colgroup.appendChild(col);
  }
  table.appendChild(colgroup);

  const thead = table.createTHead().insertRow();
  thead.appendChild(document.createElement('th'));
  for (let c = range.s.c; c <= lastCol; c++) {
    thead.appendChild(document.createElement('th')).textContent = colName(c);
  }

  const tbody = table.createTBody();
  const rowsInfo = ws['!rows'] || [];
  for (let r = range.s.r; r <= lastRow; r++) {
    const tr = tbody.insertRow();
    if (rowsInfo[r] && rowsInfo[r].hidden) tr.hidden = true;
    tr.appendChild(document.createElement('th')).textContent = r + 1;
    for (let c = range.s.c; c <= lastCol; c++) {
      const key = r + ':' + c;
      if (covered.has(key)) continue;
      const td = tr.insertCell();
      const sp = span.get(key);
      if (sp) { td.rowSpan = sp.rows; td.colSpan = sp.cols; td.className = 'merged'; }
      const cell = ws[XLSX.utils.encode_cell({ r, c })];
      if (!cell) continue;
      if (cell.t === 'z') {
        if (!cell.f) continue;
        // Formula saved without a cached result (e.g. written by a script).
        td.textContent = '=' + cell.f;
        td.classList.add('formula');
      } else {
        td.textContent = cell.w !== undefined ? cell.w : String(cell.v);
        if (cell.t === 'n' || cell.t === 'd') td.classList.add('num');
        if (cell.t === 'e') td.classList.add('err');
      }
      if (cell.f) td.title = '=' + cell.f;
    }
  }
  frag.appendChild(table);

  if (lastRow < range.e.r || lastCol < range.e.c) {
    const p = document.createElement('p');
    p.className = 'sheet-note';
    p.textContent = `Показано перші ${lastRow - range.s.r + 1} рядків і ${lastCol - range.s.c + 1} стовпців із ${range.e.r - range.s.r + 1} × ${range.e.c - range.s.c + 1}.`;
    frag.appendChild(p);
  }
  return frag;
}

// --- PowerPoint via pptx-preview, all slides in a vertical list.
register(['pptx', 'pptm', 'potx', 'ppsx'], async (src, box) => {
  await loadScript('lib/pptx-preview.bundle.js');
  const host = document.createElement('div');
  host.className = 'pptx-host';
  box.appendChild(host);
  const width = Math.max(480, Math.min(1100, el.viewport.clientWidth - 48));
  const previewer = pptxPreview.init(host, { width, height: Math.round(width * 9 / 16), mode: 'list' });
  await previewer.preview(await src.buffer());
});

// --- RTF via rtf.js (with WMF/EMF picture support), shown as a page.
register(['rtf'], async (src, box) => {
  await loadScript('lib/WMFJS.bundle.min.js');
  await loadScript('lib/EMFJS.bundle.min.js');
  await loadScript('lib/RTFJS.bundle.min.js');
  RTFJS.loggingEnabled(false);
  WMFJS.loggingEnabled(false);
  EMFJS.loggingEnabled(false);
  const page = document.createElement('article');
  page.className = 'paper';
  page.append(...await new RTFJS.Document(await src.buffer()).render());
  box.appendChild(page);
});
