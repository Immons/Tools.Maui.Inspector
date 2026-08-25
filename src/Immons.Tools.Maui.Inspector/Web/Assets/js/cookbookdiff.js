// Baseline & change detection for the cookbook: remember every preview as it is, then — after
// a style or resource edit — mark the tiles whose pixels changed (PNG is lossless, so equal
// pixels give equal bytes) and let a hover flip a changed tile back to its "before" image.

let cookbookBaselineHashes = null;   // item id → { hash, url }
let cookbookChangedOnly = false;

function cookbookHashBytes(bytes) {
  let h = 0x811c9dc5;
  for (let i = 0; i < bytes.length; i++) {
    h ^= bytes[i];
    h = Math.imul(h, 0x01000193) >>> 0;
  }
  return h.toString(16) + ':' + bytes.length;
}

function cookbookHashText(text) {
  return cookbookHashBytes(new TextEncoder().encode(text || ''));
}

// Registers the current preview of a card; baseline URLs stay alive until the baseline is replaced.
function cookbookRecord(card, id, hash, url) {
  const previous = cookbookBlobs.get(id);
  if (previous && previous.url && !(cookbookBaselineHashes && cookbookBaselineHashes.get(id)?.url === previous.url))
    URL.revokeObjectURL(previous.url);
  cookbookBlobs.set(id, { hash: hash, url: url });
  cookbookMarkChanged(card, id, hash);
}

function cookbookMarkChanged(card, id, hash) {
  if (!cookbookBaselineHashes || !card) return;
  const base = cookbookBaselineHashes.get(id);
  card.classList.toggle('changed', !!base && base.hash !== hash);
  card.classList.toggle('newtile', !base);
  card.classList.remove('showingbase');
  if (cookbookChangedOnly) filterCookbook(cookbookQuery);
  setCookHint(cookbookStatus());
}

function cookbookBaseline() {
  cookbookClearBaseline();
  cookbookBaselineHashes = new Map();
  for (const [id, entry] of cookbookBlobs) cookbookBaselineHashes.set(id, { hash: entry.hash, url: entry.url });
  for (const card of document.querySelectorAll('#cookgrid .cookcard')) card.classList.remove('changed', 'newtile', 'showingbase');
  document.getElementById('cookChangedBtn').hidden = false;
  document.getElementById('cookBaselineBtn').classList.add('active');
  flashHint('baseline: ' + cookbookBaselineHashes.size + ' previews remembered — edit a style and watch the tiles that change');
  setCookHint(cookbookStatus());
}

function cookbookClearBaseline() {
  if (cookbookBaselineHashes) {
    for (const [id, entry] of cookbookBaselineHashes)
      if (entry.url && cookbookBlobs.get(id)?.url !== entry.url) URL.revokeObjectURL(entry.url);
  }
  cookbookBaselineHashes = null;
  cookbookChangedOnly = false;
  const changedBtn = document.getElementById('cookChangedBtn');
  if (changedBtn) { changedBtn.hidden = true; changedBtn.classList.remove('active'); }
  document.getElementById('cookBaselineBtn')?.classList.remove('active');
}

function cookbookChangedCount() {
  return document.querySelectorAll('#cookgrid .cookcard.changed').length;
}

function toggleChangedOnly() {
  cookbookChangedOnly = !cookbookChangedOnly;
  document.getElementById('cookChangedBtn').classList.toggle('active', cookbookChangedOnly);
  filterCookbook(cookbookQuery);
}

// Hover on a changed tile shows the baseline image; leaving restores the current one.
function cookbookShowBaseline(card, show) {
  if (!cookbookBaselineHashes || !card.classList.contains('changed')) return;
  const id = card.dataset.id;
  const base = cookbookBaselineHashes.get(id);
  const current = cookbookBlobs.get(id);
  const img = card.querySelector('img');
  if (!img || !base || !base.url || !current || !current.url) return;
  img.src = show ? base.url : current.url;
  card.classList.toggle('showingbase', show);
}
