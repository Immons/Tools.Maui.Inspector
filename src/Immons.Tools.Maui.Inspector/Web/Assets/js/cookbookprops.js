// Focus popup for a cookbook item: the sample alone at full width (or the width the control
// declares) — rendered off screen on the device, or on a page of its own when the gallery is open
// there — captured here. ▤ opens the property sheet underneath on demand (own properties first,
// inherited folded); edits apply live, the capture and the card re-capture.
let cookbookFocusItem = null;
let cookbookFocusElement = null;
let cookbookFocusTimer = null;
let cookbookFocusZoom = 1;        // css px per dp of the capture
let cookbookFocusZoomAuto = true; // until the user picks a zoom, each load fits wide captures and keeps small ones at 100%

async function openCookbookFocus(item) {
  const back = document.getElementById('cookpropsback');
  document.getElementById('cookpropstitle').textContent = item.name + ' · ' + (item.targetType || item.kind);
  document.getElementById('cookpropshint').textContent = 'opening on the device…';
  const body = document.getElementById('cookpropsbody');
  body.innerHTML = '';
  document.getElementById('cookfocusimg').removeAttribute('src');
  back.hidden = false;
  cookbookFocusItem = item;
  cookbookFocusZoomAuto = true;

  setCookbookSheet(false);
  const focus = await focusCookbookItem(item);
  if (!focus || !focus.elementId) {
    document.getElementById('cookpropshint').textContent = focus && focus.error ? '⚠ ' + focus.error : 'the sample could not be opened on the device';
    return;
  }
  cookbookFocusElement = focus.elementId;
  document.getElementById('cookpropshint').textContent = (focus.onDevice ? 'on its own page on the device' : 'rendered off screen, nothing shown on the device')
    + ' — ▤ opens the properties, edits apply live';
  await loadFocusPreview();
}

// The sheet stays out of the way until asked for — on the device as well.
function toggleCookbookSheet() {
  setCookbookSheet(document.getElementById('cookpropsbody').hidden);
}

async function setCookbookSheet(shown) {
  const body = document.getElementById('cookpropsbody');
  body.hidden = !shown;
  document.getElementById('cookSheetBtn').classList.toggle('active', shown);
  propsTarget = shown ? body : null;
  if (shown && cookbookFocusElement) {
    body.innerHTML = '';
    await loadProps(cookbookFocusElement, false);
  }
}

function closeCookbookProps() {
  document.getElementById('cookpropsback').hidden = true;
  propsTarget = null;
  cookbookFocusItem = null;
  cookbookFocusElement = null;
  fetch('/api/cookbook/focus', { method: 'POST', body: JSON.stringify({ id: null }) });
}

// Opens the item's own page on the device and waits for its sample to exist.
async function focusCookbookItem(item) {
  await fetch('/api/cookbook/focus', { method: 'POST', body: JSON.stringify({ id: item.id }) });
  for (let attempt = 0; attempt < 16; attempt++) {
    await new Promise(r => setTimeout(r, 250));
    await syncCookbookMeta();
    const focus = cookbook && cookbook.focus;
    if (focus && focus.item === item.id && (focus.elementId || focus.error)) return focus;
  }
  return cookbook && cookbook.focus;
}

async function loadFocusPreview() {
  const item = cookbookFocusItem;
  if (!item) return;
  const img = document.getElementById('cookfocusimg');
  try {
    const r = await fetch('/api/cookbook/preview?id=' + encodeURIComponent(item.id) + '&focus=1&t=' + Date.now());
    if (!r.ok) return;
    const bytes = new Uint8Array(await r.arrayBuffer());
    const url = URL.createObjectURL(new Blob([bytes], { type: 'image/png' }));
    img.dataset.hash = cookbookHashBytes(bytes);
    img.onload = () => {
      if (cookbookFocusZoomAuto) cookbookFocusZoom = Math.min(1, fitCookbookZoom());
      applyCookbookZoom();
      URL.revokeObjectURL(img.dataset.prev || '');
      img.dataset.prev = url;
    };
    img.src = url;
  } catch { /* the device is busy — the next edit refreshes it */ }
}

// Zoom: 'fit' or a factor (css px per dp); the capture is retina, so 100% is crisp.
function setCookbookZoom(zoom) {
  cookbookFocusZoomAuto = false;
  cookbookFocusZoom = zoom === 'fit' ? fitCookbookZoom() : Math.max(0.25, Math.min(4, +zoom || 1));
  applyCookbookZoom();
}

function fitCookbookZoom() {
  const img = document.getElementById('cookfocusimg');
  const dpWidth = img.naturalWidth / ((cookbook && cookbook.scale) || 1);
  const box = document.getElementById('cookfocuspreview');
  return dpWidth > 0 ? Math.max(0.25, Math.min(4, (box.clientWidth - 34) / dpWidth)) : 1;
}

function applyCookbookZoom() {
  const img = document.getElementById('cookfocusimg');
  if (!img.naturalWidth) return;
  const dpWidth = img.naturalWidth / ((cookbook && cookbook.scale) || 1);
  img.style.width = Math.round(dpWidth * cookbookFocusZoom) + 'px';
  document.getElementById('cookzoomrange').value = Math.round(cookbookFocusZoom * 100);
  document.getElementById('cookzoomval').textContent = Math.round(cookbookFocusZoom * 100) + '%';
}

function toggleCookbookMax() {
  const panel = document.getElementById('cookpropspanel');
  panel.classList.toggle('maximized');
  document.getElementById('cookMaxBtn').classList.toggle('active', panel.classList.contains('maximized'));
  if (cookbookFocusZoomAuto) { cookbookFocusZoom = Math.min(1, fitCookbookZoom()); applyCookbookZoom(); }
}

// Ctrl/⌘ + wheel zooms the preview; dragging pans it (the box scrolls).
(() => {
  const box = document.getElementById('cookfocuspreview');
  if (!box) return;
  box.addEventListener('wheel', (e) => {
    if (!(e.ctrlKey || e.metaKey)) return;
    e.preventDefault();
    setCookbookZoom(cookbookFocusZoom * (e.deltaY < 0 ? 1.1 : 0.9));
  }, { passive: false });
  let pan = null;
  box.addEventListener('pointerdown', (e) => {
    if (e.button !== 0) return;
    pan = { x: e.clientX, y: e.clientY, left: box.scrollLeft, top: box.scrollTop };
    box.classList.add('panning');
    box.setPointerCapture(e.pointerId);
  });
  box.addEventListener('pointermove', (e) => {
    if (!pan) return;
    box.scrollLeft = pan.left - (e.clientX - pan.x);
    box.scrollTop = pan.top - (e.clientY - pan.y);
  });
  const stop = () => { pan = null; box.classList.remove('panning'); };
  box.addEventListener('pointerup', stop);
  box.addEventListener('pointercancel', stop);
})();

// Called with the card previews after an edit — the focused instance changed too.
function cookbookFocusOnEdit() {
  if (!cookbookFocusItem) return;
  clearTimeout(cookbookFocusTimer);
  cookbookFocusTimer = setTimeout(loadFocusPreview, 400);
}

// Shows the item in the device's list (its section and page, scrolled into view) and waits for
// its tile — used by Inspect, which selects the tile in the tree.
async function realizeCookbookItem(item) {
  let live = cookbook.sections.flatMap(s => s.items).find(i => i.id === item.id) || item;
  if (live.elementId) return live;
  await fetch('/api/cookbook/open', { method: 'POST', body: JSON.stringify({ on: true, section: item.section || null, item: item.id }) });
  for (let attempt = 0; attempt < 12; attempt++) {
    await new Promise(r => setTimeout(r, 250));
    await syncCookbookMeta();
    live = cookbook.sections.flatMap(s => s.items).find(i => i.id === item.id);
    if (live && live.elementId) return live;
  }
  return live;
}
