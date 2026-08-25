// Visual tree: rendering, selection marks, search filter, expand/collapse.

async function refreshAll(skipProps) {
  const r = await fetch('/api/tree');
  const data = await r.json();
  document.getElementById('win').textContent =
    (data.device ? data.device + '   ·   ' : '') + (data.window ? 'window ' + data.window + ' dp' : '');
  window.deviceStr = data.device || '';
  window.devicePort = data.port || null;
  window.adaptiveAvailable = data.adaptive === true;
  if (data.window) {
    const wm = data.window.match(/^([\d.]+)×([\d.]+)$/);
    if (wm) windowDp = [parseFloat(wm[1]), parseFloat(wm[2])];
  }
  const tree = document.getElementById('tree');
  tree.innerHTML = '';
  for (const root of data.roots) tree.appendChild(renderNode(root, 0));
  if (selectedId != null) {
    reveal(selectedId);
    markRows();
    if (!skipProps) loadProps(selectedId, false);
  }
}

function renderNode(n, depth) {
  const div = document.createElement('div');
  div.className = 'node';
  if (depth > 6 && n.children) div.classList.add('collapsed');
  const row = document.createElement('div');
  row.className = 'row';
  row.dataset.id = n.id;
  row.dataset.search = n.s || n.label.toLowerCase();
  row.draggable = true; // same-parent reorder, handled in structure.js

  const caret = document.createElement('span');
  caret.className = 'caret';
  caret.textContent = n.children ? (div.classList.contains('collapsed') ? '▸' : '▾') : '';
  caret.onclick = (e) => { e.stopPropagation(); div.classList.toggle('collapsed');
    caret.textContent = div.classList.contains('collapsed') ? '▸' : '▾'; };
  row.appendChild(caret);

  const label = document.createElement('span');
  label.textContent = n.label;
  row.appendChild(label);
  row.onclick = () => onRowClick(n.id);
  div.appendChild(row);

  if (n.children) {
    const kids = document.createElement('div');
    kids.className = 'kids';
    for (const c of n.children) kids.appendChild(renderNode(c, depth + 1));
    div.appendChild(kids);
  }
  return div;
}

function markRows() {
  document.querySelectorAll('.row.selected, .row.compare').forEach(e => e.classList.remove('selected', 'compare'));
  const sel = document.querySelector('.row[data-id="' + selectedId + '"]');
  if (sel) sel.classList.add('selected');
  if (compareId != null) {
    const cmp = document.querySelector('.row[data-id="' + compareId + '"]');
    if (cmp) cmp.classList.add('compare');
  }
}

function reveal(id) {
  const row = document.querySelector('.row[data-id="' + id + '"]');
  if (!row) return false;
  let p = row.parentElement;
  while (p && p.id !== 'tree') {
    if (p.classList.contains('node') && p.classList.contains('collapsed')) {
      p.classList.remove('collapsed');
      const c = p.querySelector(':scope > .row > .caret');
      if (c) c.textContent = '▾';
    }
    p = p.parentElement;
  }
  row.scrollIntoView({ block: 'nearest' });
  return true;
}

function setAllCollapsed(collapsed) {
  document.querySelectorAll('#tree .node').forEach(n => {
    if (!n.querySelector(':scope > .kids')) return;
    n.classList.toggle('collapsed', collapsed);
    const c = n.querySelector(':scope > .row > .caret');
    if (c) c.textContent = collapsed ? '▸' : '▾';
  });
  if (!collapsed && selectedId != null) reveal(selectedId);
}

function applyFilter(q) {
  q = q.trim().toLowerCase();
  const roots = document.querySelectorAll('#tree > .node');
  if (!q) {
    document.querySelectorAll('#tree .node').forEach(n => n.classList.remove('hidden', 'hit'));
    return;
  }
  function visit(node) {
    const row = node.querySelector(':scope > .row');
    const self = row && (row.dataset.search || '').includes(q);
    let childHit = false;
    node.querySelectorAll(':scope > .kids > .node').forEach(k => { if (visit(k)) childHit = true; });
    node.classList.toggle('hidden', !(self || childHit));
    node.classList.toggle('hit', !!self);
    if (childHit && node.classList.contains('collapsed')) {
      node.classList.remove('collapsed');
      const c = node.querySelector(':scope > .row > .caret');
      if (c) c.textContent = '▾';
    }
    return self || childHit;
  }
  roots.forEach(visit);
}

async function onRowClick(id) {
  if (measure && selectedId != null && id !== selectedId) {
    compareId = id;
    markRows();
    fetch('/api/measure', { method: 'POST', body: JSON.stringify({ primary: selectedId, compare: id }) });
    return;
  }

  selectedId = id;
  compareId = null;
  markRows();
  fetch('/api/element/' + id + '/select', { method: 'POST' });
  await loadProps(id, false);
}
