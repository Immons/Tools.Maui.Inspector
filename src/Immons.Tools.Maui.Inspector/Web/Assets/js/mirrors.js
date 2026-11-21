// Multi-device hot reload: edits made in this panel are mirrored to sibling apps
// (same build on other simulators/devices), addressed by XAML source identity —
// stable across platforms and idioms, unlike per-process element ids.
let mirrorTargets = [];
try { mirrorTargets = JSON.parse(localStorage.getItem('hvMirrors') || '[]'); } catch { }

function saveMirrors() {
  localStorage.setItem('hvMirrors', JSON.stringify(mirrorTargets));
}

async function scanMirrors() {
  const found = [];
  // Default pool + ports of already-known targets + whatever the input names:
  // "9500" | "9500,9600" | "9400-9420" — custom adb forwards live outside the pool.
  const ports = new Set();
  for (let port = 9295; port <= 9309; port++) ports.add(port);
  for (const t of mirrorTargets) {
    const m = t.url.match(/:(\d+)$/);
    if (m) ports.add(parseInt(m[1], 10));
  }
  const spec = document.getElementById('mirroraddr').value.trim();
  for (const part of spec.split(',')) {
    const range = part.trim().match(/^(\d+)(?:-(\d+))?$/);
    if (!range) continue;
    const from = parseInt(range[1], 10);
    const to = Math.min(parseInt(range[2] || range[1], 10), from + 100);
    for (let port = from; port <= to; port++) ports.add(port);
  }
  // This page's host on those ports, plus devices on the network the header picker found or was
  // given ("Scan local network…" / "Add address…") — a physical phone mirrors like a simulator.
  const urls = [...ports].sort().map(port => 'http://' + location.hostname + ':' + port);
  for (const hostPort of savedInspectorHosts()) urls.push('http://' + hostPort);
  const own = await pingInspector(location.origin, 1500);
  const seen = new Set(own ? [own.instance] : []);
  const answers = await Promise.all([...new Set(urls)].filter(u => u !== location.origin)
    .map(async url => ({ url, d: await pingInspector(url, 700) })));
  for (const { url, d } of answers) {
    if (!d || seen.has(d.instance)) continue;   // same app reachable twice (localhost + LAN address)
    seen.add(d.instance);
    found.push({ url, label: inspectorLabel(d) });
  }
  for (const f of found) {
    const existing = mirrorTargets.find(t => t.url === f.url);
    if (existing) existing.label = f.label;
    else mirrorTargets.push({ ...f, on: true });
  }
  saveMirrors();
  renderMirrors();
}

function addMirrorManually() {
  const value = document.getElementById('mirroraddr').value.trim();
  if (!value) return;
  const url = value.startsWith('http') ? value : 'http://' + value;
  if (!mirrorTargets.find(t => t.url === url))
    mirrorTargets.push({ url: url, label: url, on: true });
  document.getElementById('mirroraddr').value = '';
  saveMirrors();
  renderMirrors();
}

function renderMirrors() {
  const list = document.getElementById('mirrorslist');
  list.innerHTML = mirrorTargets.length ? '' : ' (no targets — Scan or add host:port)';
  for (const t of mirrorTargets) {
    const item = document.createElement('label');
    item.className = 'mirroritem';
    const cb = Object.assign(document.createElement('input'), { type: 'checkbox', checked: t.on });
    cb.onchange = () => { t.on = cb.checked; saveMirrors(); };
    const del = Object.assign(document.createElement('button'), { textContent: '✕', className: 'clearbtn' });
    del.onclick = (e) => {
      e.preventDefault();
      mirrorTargets = mirrorTargets.filter(x => x !== t);
      saveMirrors();
      renderMirrors();
    };
    const text = document.createElement('span');
    text.className = 'mirrortext';
    const name = document.createElement('span');
    name.textContent = ' ' + t.label + ' ';
    // The address matters: ports get recycled between apps, so the label alone is ambiguous.
    const addr = document.createElement('span');
    addr.className = 'mirrorurl';
    addr.textContent = t.url.replace(/^https?:\/\//, '');
    text.append(name, addr);
    item.append(cb, text, ' ', del);
    item.dataset.url = t.url;
    list.appendChild(item);
  }
  probeMirrors();
}

// Marks each target reachable or not — a stale entry pointing at a closed app looks identical
// to a live one until you try to use it.
async function probeMirrors() {
  for (const item of document.querySelectorAll('#mirrorslist .mirroritem')) {
    const url = item.dataset.url;
    if (!url) continue;
    let up = false;
    try {
      const ctl = new AbortController();
      setTimeout(() => ctl.abort(), 900);
      up = (await fetch(url + '/api/ping', { signal: ctl.signal })).ok;
    } catch { /* unreachable */ }
    item.classList.toggle('up', up);
    item.classList.toggle('down', !up);
    item.title = up ? 'Reachable at ' + url : 'No answer from ' + url;
    const target = mirrorTargets.find(t => t.url === url);
    if (target) target.up = up;
  }
  const cleanup = document.getElementById('mirrorclean');
  if (cleanup) {
    const dead = mirrorTargets.filter(t => t.up === false).length;
    cleanup.hidden = dead === 0;
    cleanup.textContent = '🧹 Remove ' + dead + ' unreachable';
  }
}

// Ports are recycled between runs, so a list left alone fills up with entries pointing at apps
// that no longer exist. They stay checked and edits sent to them go nowhere.
function removeUnreachableMirrors() {
  mirrorTargets = mirrorTargets.filter(t => t.up !== false);
  saveMirrors();
  renderMirrors();
}

// Fan-out core: posts the payload to every enabled target and flashes per-target results.
async function mirrorFanOut(path, payload) {
  const skipped = mirrorTargets.filter(t => t.on && t.up === false).length;
  const active = mirrorTargets.filter(t => t.on && t.up !== false).slice();
  // "All instances" re-applies the edit locally through the same source-identity matcher,
  // so every element created from that XAML line (all DataTemplate rows) is updated.
  // The same applies while the device picker points elsewhere: the edit went to the remote
  // app directly, and this portal's own device must not silently fall behind.
  if ((allInstances || window.apiBase) && path !== '/api/mock/rules/scenario')
    active.push({ url: location.origin, label: 'this device', on: true });
  if (!active.length) return;
  const results = [];
  await Promise.all(active.map(async t => {
    try {
      const r = await (await fetch(t.url + path, { method: 'POST', body: JSON.stringify(payload) })).json();
      results.push(shortLabel(t) + (r.applied === undefined ? ' ✓' : r.applied > 0 ? ' ✓' + r.applied : ' —'));
    } catch {
      results.push(shortLabel(t) + ' ✗offline');
    }
  }));
  const hint = document.getElementById('hint');
  hint.textContent = '🖧 ' + results.join('  ·  ')
    + (skipped ? '   ·   ' + skipped + ' unreachable skipped' : '');
  setTimeout(() => { if (hint.textContent.startsWith('🖧')) updateHint(); }, 4000);
}

// Called by apply() after a successful local edit; targets match by XAML source identity.
function mirrorApply(section, name, value, clear) {
  if (!currentSource) return;
  mirrorFanOut('/api/broadcast/property',
    { source: currentSource, ...currentIdentity, section: section, name: name, value: value, clear: !!clear });
}

// Structural actions: add/remove span, shadow, grid definitions, create FormattedText…
function mirrorAction(section, name) {
  if (!currentSource) return;
  mirrorFanOut('/api/broadcast/action', { source: currentSource, ...currentIdentity, section: section, name: name });
}

// Mock rules are device state, not element state — mirroring replaces each target's set
// with this device's set (ids are per-device, so it's a wipe-and-recreate sync).
async function mirrorRulesSync() {
  const active = mirrorTargets.filter(t => t.on);
  if (!active.length) return;
  const mine = await (await fetch('/api/mock/rules')).json();
  await Promise.all(active.map(async t => {
    try {
      const theirs = await (await fetch(t.url + '/api/mock/rules')).json();
      for (const r of theirs.rules)
        await fetch(t.url + '/api/mock/rules/delete', { method: 'POST', body: JSON.stringify({ id: r.id }) });
      for (const name of (theirs.scenarios || []).filter(n => !(mine.scenarios || []).includes(n)))
        await fetch(t.url + '/api/mock/rules/scenario/remove', { method: 'POST', body: JSON.stringify({ name: name }) });
      for (const name of (mine.scenarios || []))
        await fetch(t.url + '/api/mock/rules/scenario/add', { method: 'POST', body: JSON.stringify({ name: name }) });
      for (const r of mine.rules)
        await fetch(t.url + '/api/mock/rules/save', { method: 'POST', body: JSON.stringify({ ...r, id: 0 }) });
      await fetch(t.url + '/api/mock/rules/scenario', { method: 'POST', body: JSON.stringify({ name: mine.activeScenario || '' }) });
    } catch { /* target offline — it will pick rules up on the next sync */ }
  }));
}

function shortLabel(t) {
  return (t.label.split('·')[1] || t.label).trim().split(' ').slice(0, 2).join(' ');
}
