// Design cookbook: the app's styles, controls, colors, fonts, images and templates as live
// samples, listed as cards with a PNG captured tile by tile. The captures render headlessly on
// the device (an off-screen stage with the app's styles) — nothing appears on its screen unless
// the gallery page is opened on purpose (📱 Open on device, or ▤ Panel → 📚 Cookbook).

let cookbook = null;              // last /api/cookbook payload
let cookbookSection = null;       // section id filter, '' = all
let cookbookQuery = '';
let cookbookStale = false;        // an edit happened while the view was hidden
let cookbookLoadSeq = 0;          // cancels preview runs superseded by a newer one
let cookbookEditTimer = null;
const cookbookBlobs = new Map();  // item id → { hash, url } of the current preview (url null for CSS swatches)

async function showCookbook() {
  await refreshCookbook(cookbookStale);
  cookbookStale = false;
}

async function refreshCookbook(reloadPreviews) {
  let data;
  try {
    data = await (await fetch('/api/cookbook')).json();
  } catch {
    setCookHint('App not reachable.');
    return;
  }
  cookbook = data;
  registerSwatchHashes(data);
  if (cookbookSection === null || (cookbookSection && !data.sections.some(s => s.id === cookbookSection)))
    cookbookSection = data.sections.length ? data.sections[0].id : '';
  updateCookbookBar();
  renderCookbookChips();
  renderCookbookGrid();
  loadCookbookPreviews(reloadPreviews);
}

// Solid colors never need a device capture: their hash is the hex, known for every item at once.
function registerSwatchHashes(data) {
  for (const section of data.sections) {
    for (const item of section.items) {
      if ((item.kind === 'color' || item.kind === 'brush') && item.value)
        cookbookRecord(document.querySelector('#cookgrid .cookcard[data-id="' + CSS.escape(item.id) + '"]'), item.id, cookbookHashText(item.value), null);
    }
  }
}

function updateCookbookBar() {
  const open = !!(cookbook && cookbook.open);
  const btn = document.getElementById('cookOpenBtn');
  btn.classList.toggle('active', open);
  btn.textContent = open ? '📱 Close on device' : '📱 Open on device';
  for (const t of ['system', 'light', 'dark'])
    document.getElementById('theme-' + t).classList.toggle('active', cookbook && cookbook.theme === t);
}

async function toggleCookbookOnDevice() {
  await setCookbookOnDevice(!(cookbook && cookbook.open));
}

async function setCookbookOnDevice(on) {
  try {
    await fetch('/api/cookbook/open', { method: 'POST', body: JSON.stringify({ on: on, section: on ? (cookbookSection || null) : null }) });
  } catch { /* reported by the refresh */ }
  await refreshCookbook(false);
}

async function setAppTheme(theme) {
  await fetch('/api/theme', { method: 'POST', body: JSON.stringify({ theme: theme }) });
  if (cookbook) cookbook.theme = theme;
  updateCookbookBar();
  cookbookOnEdit();
}

function renderCookbookChips() {
  const host = document.getElementById('cookchips');
  host.innerHTML = '';
  const chip = (id, label) => {
    const b = document.createElement('button');
    b.textContent = label;
    b.classList.toggle('active', cookbookSection === id);
    b.onclick = () => {
      cookbookSection = id;
      renderCookbookChips();
      renderCookbookGrid();
      // The gallery page, when open on the device, follows the panel's section; the previews never need it.
      if (cookbook.open && id) fetch('/api/cookbook/open', { method: 'POST', body: JSON.stringify({ on: true, section: id }) });
      loadCookbookPreviews(false);
    };
    host.appendChild(b);
  };
  chip('', 'All');
  for (const s of cookbook.sections) chip(s.id, s.title + ' · ' + s.items.length);
}

function renderCookbookGrid() {
  const grid = document.getElementById('cookgrid');
  grid.innerHTML = '';
  for (const section of cookbook.sections) {
    if (cookbookSection && section.id !== cookbookSection) continue;
    grid.appendChild(el('h2', 'cooksection', section.title + ' · ' + section.items.length));
    const cards = el('div', 'cookcards');
    for (const item of section.items) cards.appendChild(renderCookCard(item));
    grid.appendChild(cards);
  }
  filterCookbook(cookbookQuery);
}

const COOK_RESOURCE_KINDS = ['color', 'brush', 'style', 'controltemplate', 'datatemplate', 'recipe', 'scalar', 'shadow'];

function renderCookCard(item) {
  const card = el('div', 'cookcard');
  card.dataset.id = item.id;
  card.dataset.search = [item.name, item.kind, item.targetType, item.source, item.detail, item.value]
    .filter(Boolean).join(' ').toLowerCase();

  const preview = el('div', 'cookpreview');
  if ((item.kind === 'color' || item.kind === 'brush') && item.value) {
    // Solid colors need no round trip — the hex is the truth, and it diffs exactly.
    const swatch = el('div', 'cookswatch');
    swatch.style.background = cssColor(item.value);
    swatch.title = item.value;
    preview.appendChild(swatch);
    cookbookRecord(card, item.id, cookbookHashText(item.value), null);
  } else if (item.previewable) {
    const img = document.createElement('img');
    img.alt = item.name;
    img.title = 'Captured on the device — click to open it full width with its properties, hover to compare with the baseline when it changed';
    img.style.cursor = 'pointer';
    img.onclick = () => openCookbookFocus(item);
    img.addEventListener('mouseenter', () => cookbookShowBaseline(card, true));
    img.addEventListener('mouseleave', () => cookbookShowBaseline(card, false));
    preview.appendChild(img);
    card.dataset.preview = '1';
  } else {
    preview.appendChild(el('div', 'cooknone', 'no visual form — setters only'));
  }
  card.appendChild(preview);

  const body = el('div', 'cookbody');
  const name = el('div', 'cookname');
  name.appendChild(el('span', '', item.name));
  name.appendChild(el('span', 'catalogbadge ' + item.kind, cookbookBadge(item.kind)));
  body.appendChild(name);
  if (item.detail) body.appendChild(el('div', 'cookdetail', item.detail));
  if (item.source) body.appendChild(el('div', 'cooksource', item.source));
  const error = el('div', 'cookerror', item.error ? '⚠ ' + item.error : '');
  error.hidden = !item.error;
  body.appendChild(error);

  const actions = el('div', 'cookactions');
  if (item.previewable) {
    const open = el('button', '', '⤢ Open');
    open.title = 'The sample alone at full width (or as declared) with its properties — rendered off screen unless the gallery is open on the device';
    open.onclick = () => openCookbookFocus(item);
    actions.appendChild(open);
    const inspect = el('button', '', '⌖ Inspect');
    inspect.title = 'Select the sample in the inspector tree — needs the gallery open on the device (📱 Open on device, or ▤ Panel → 📚 Cookbook)';
    inspect.onclick = () => inspectCookbookItem(item);
    actions.appendChild(inspect);
  }
  if (COOK_RESOURCE_KINDS.includes(item.kind)) {
    const edit = el('button', '', '🎨 Edit');
    edit.title = 'Open the Resources popup on this key';
    edit.onclick = () => openResourcesFor(item.name);
    actions.appendChild(edit);
    const copy = el('button', '', '⧉');
    copy.title = 'Copy {StaticResource ' + item.name + '}';
    copy.onclick = () => { navigator.clipboard?.writeText('{StaticResource ' + item.name + '}'); flashHint('copied {StaticResource ' + item.name + '}'); };
    actions.appendChild(copy);
  }
  if (item.states && item.previewable) actions.appendChild(renderStatePicker(card, item));
  body.appendChild(actions);
  card.appendChild(body);
  return card;
}

function cookbookBadge(kind) {
  return { controltemplate: 'control template', datatemplate: 'data template' }[kind] || kind;
}

// Visual states the sample declares (own or via its style) are forced on the device and re-captured.
function renderStatePicker(card, item) {
  const select = document.createElement('select');
  select.className = 'cookstates';
  select.title = 'Force a visual state on the sample';
  const fill = (states) => {
    select.innerHTML = '';
    select.appendChild(new Option('state…', ''));
    for (const s of states) select.appendChild(new Option(s, s));
    select.hidden = states.length === 0;
  };
  fill(item.visualStates || []);
  card.cookFillStates = fill;
  select.onchange = async () => {
    if (!select.value) return;
    await fetch('/api/cookbook/state', { method: 'POST', body: JSON.stringify({ id: item.id, state: select.value }) });
    setTimeout(() => loadCookbookPreview(card, true), 120);
  };
  return select;
}

// Previews of the visible cards, three at a time — each capture runs on the device's UI thread.
async function loadCookbookPreviews(force) {
  const seq = ++cookbookLoadSeq;
  const cards = [...document.querySelectorAll('#cookgrid .cookcard[data-preview="1"]')]
    .filter(c => !c.classList.contains('hiddenbyfilter') && (force || !c.dataset.loaded));
  if (!cards.length) { setCookHint(''); return; }
  let done = 0;
  const queue = cards.slice();
  const worker = async () => {
    while (queue.length && seq === cookbookLoadSeq) {
      await loadCookbookPreview(queue.shift(), force);
      setCookHint('previews ' + (++done) + ' / ' + cards.length);
    }
  };
  await Promise.all([worker(), worker(), worker()]);
  if (seq !== cookbookLoadSeq) return;
  setCookHint(cookbookStatus());
  // Tiles on the device screen carry element ids — pick them up for Inspect / Props.
  await syncCookbookMeta();
}

async function loadCookbookPreview(card, force) {
  const id = card.dataset.id;
  const img = card.querySelector('img');
  if (!img) return;
  try {
    const r = await fetch('/api/cookbook/preview?id=' + encodeURIComponent(id) + '&t=' + Date.now());
    if (!r.ok) {
      card.dataset.loaded = '1';
      setCardError(card, await r.text());
      return;
    }
    const bytes = new Uint8Array(await r.arrayBuffer());
    const states = (r.headers.get('X-Visual-States') || '').split(',').filter(Boolean);
    if (card.cookFillStates) card.cookFillStates(states);
    const url = URL.createObjectURL(new Blob([bytes], { type: 'image/png' }));
    img.onload = () => { img.style.width = Math.round(img.naturalWidth / (cookbook.scale || 1)) + 'px'; };
    img.src = url;
    card.dataset.loaded = '1';
    setCardError(card, '');
    cookbookRecord(card, id, cookbookHashBytes(bytes), url);
  } catch {
    setCardError(card, 'preview failed');
  }
}

function setCardError(card, text) {
  const error = card.querySelector('.cookerror');
  if (!error) return;
  error.textContent = text ? '⚠ ' + text : '';
  error.hidden = !text;
}

// Element ids exist once the device built a section; re-read them without re-rendering the cards.
async function syncCookbookMeta() {
  let data;
  try { data = await (await fetch('/api/cookbook')).json(); } catch { return; }
  cookbook = data;
  for (const section of data.sections) {
    for (const item of section.items) {
      const card = document.querySelector('#cookgrid .cookcard[data-id="' + CSS.escape(item.id) + '"]');
      if (!card) continue;
      card.cookItem = item;
      // States come with the tile on screen or with a capture's header — never clear what a capture reported.
      if (card.cookFillStates && item.visualStates) card.cookFillStates(item.visualStates);
      if (item.error) setCardError(card, item.error);
    }
  }
}

async function inspectCookbookItem(item) {
  if (!cookbook || !cookbook.open) {
    flashHint('Inspect needs the gallery on the device screen — 📱 Open on device, or ▤ Panel → 📚 Cookbook');
    return;
  }
  const live = await realizeCookbookItem(item);
  if (!live || !live.elementId) { flashHint('the sample is not on the device screen'); return; }
  showView('inspector');
  await refreshAll(true);
  onRowClick(live.elementId);
  reveal(live.elementId);
}

function openResourcesFor(key) {
  showView('inspector');
  const search = document.getElementById('ressearch');
  search.value = key;
  if (document.getElementById('resback').hidden) toggleResources();
  else filterResources(key);
}

function filterCookbook(query) {
  cookbookQuery = (query || '').trim().toLowerCase();
  for (const cards of document.querySelectorAll('#cookgrid .cookcards')) {
    let any = false;
    for (const card of cards.querySelectorAll('.cookcard')) {
      const hit = (!cookbookQuery || (card.dataset.search || '').includes(cookbookQuery))
        && (!cookbookChangedOnly || card.classList.contains('changed'));
      card.classList.toggle('hiddenbyfilter', !hit);
      if (hit) any = true;
    }
    cards.classList.toggle('hiddenbyfilter', !any);
    if (cards.previousElementSibling) cards.previousElementSibling.classList.toggle('hiddenbyfilter', !any);
  }
}

// Called after any edit that can change what the device renders (resource/setter/property
// edits, undo, theme) — re-captures the visible previews, debounced.
function cookbookOnEdit() {
  if (activeView !== 'cookbook') { cookbookStale = true; return; }
  if (!cookbook) return; // previews render headlessly — the device page is not required
  cookbookFocusOnEdit();
  clearTimeout(cookbookEditTimer);
  cookbookEditTimer = setTimeout(() => loadCookbookPreviews(true), 450);
}

// A device switch invalidates every preview — they were captured on the other app.
function cookbookReset() {
  for (const entry of cookbookBlobs.values()) if (entry.url) URL.revokeObjectURL(entry.url);
  cookbookBlobs.clear();
  cookbookClearBaseline();
  cookbook = null;
  cookbookSection = null;
  if (activeView === 'cookbook') refreshCookbook(true);
}

function cookbookStatus() {
  if (!cookbook) return '';
  const total = cookbook.sections.reduce((n, s) => n + s.items.length, 0);
  const view = cookbook.view;
  const showing = view && view.section
    ? 'device shows ' + view.section + (view.pages > 1 ? ' · page ' + (view.page + 1) + '/' + view.pages : '')
    : 'rendered off screen · nothing shown on the device';
  return total + ' items · ' + showing
    + (cookbookBaselineHashes ? ' · baseline: ' + cookbookChangedCount() + ' changed' : '');
}

function setCookHint(text) {
  document.getElementById('cookhint').textContent = text;
}
