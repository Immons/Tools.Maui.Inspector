// Watch mode and what grows out of it: the navigation ledger (every page's pop and whether it was
// collected), the snapshot history chart, the decoded images, the bisection aids and the exports.
let memLedger = null;
let memHistory = [];
let memImages = null;
let memSettings = null;

function applyMemorySettings(settings) {
  const was = memSettings ? memSettings.tracking !== false : null;
  memSettings = settings;
  // Everything in this view stands on the tracker; when it is off, say so instead of showing stale numbers.
  const tracking = settings.tracking !== false;
  document.getElementById('memTrackBtn').classList.toggle('active', tracking);
  document.getElementById('memTrackBtn').textContent = tracking ? '⏻ Tracking' : '⏻ Tracking off';
  for (const id of ['memSnapBtn', 'memWatchBtn', 'memBaselineBtn'])
    document.getElementById(id).classList.toggle('unavailable', !tracking);
  document.getElementById('memWatchBtn').classList.toggle('active', !!settings.watch);
  document.getElementById('memBisectHandlers').checked = !!settings.disconnectHandlersOnPop;
  document.getElementById('memBisectContext').checked = !!settings.clearBindingContextOnPop;

  // The state can also change from elsewhere (another panel, the on-device pane, a fresh page
  // load) — the sections below have to follow it, not only the click that caused it.
  if (was !== tracking) {
    restartMemoryPolling();
    renderMemoryTables();
  }
}

async function postMemorySettings(body) {
  const data = await (await fetch('/api/memory/settings', { method: 'POST', body: JSON.stringify(body) })).json();
  applyMemorySettings(data);
}

// Turning tracking off empties the registry too: from that moment the inspector holds nothing of
// the app's and hooks nothing new, which is the honest answer to "is this thing slowing me down?".
async function toggleTracking() {
  const on = !(memSettings && memSettings.tracking !== false);
  await postMemorySettings({ tracking: on });
  document.getElementById('memhint').textContent = on
    ? 'tracking on — new elements, view models and handlers are recorded again (a weak reference each)'
    : 'tracking off — nothing is recorded, the registry is empty, watch mode is off. The readings above keep updating: '
      + 'they are read per request (~5 ms on a background thread, never the UI thread) and only while this tab is open — '
      + 'now every 5 s instead of every second';
  if (!on) {
    memSnapshot = null;
    memLedger = null;
    memHistory = [];
    memImages = null;
    for (const id of ['memledger', 'memhistory', 'memimages'])
      document.getElementById(id).innerHTML = '';
  } else {
    loadSnapshot();
    loadLedger();
    loadHistory();
    loadImages();
  }
  renderMemoryTables();
}

function toggleWatch() {
  if (memTrackingOff()) return;
  postMemorySettings({ watch: !(memSettings && memSettings.watch) }).then(() => {
    document.getElementById('memhint').textContent = memSettings.watch
      ? `watch mode on — a snapshot ${Math.round(memSettings.watchDelayMs / 1000)} s after every page pop, verdicts in the ledger`
      : 'watch mode off';
  });
}

function setBisection(key, on) {
  postMemorySettings({ [key]: on });
}

// Baseline: mark the state, run the flow, snapshot — the growth per repetition is the verdict.
let memHasBaseline = false;

function memTrackingOff() {
  return !!(memSettings && memSettings.tracking === false);
}

async function toggleBaseline() {
  if (memTrackingOff()) return;
  const clear = memHasBaseline;
  document.getElementById('memhint').textContent = clear ? 'clearing the baseline…' : 'marking the baseline (a snapshot first)…';
  const data = await (await fetch('/api/memory/baseline', { method: 'POST', body: JSON.stringify({ clear: clear }) })).json();
  memHasBaseline = data.hasBaseline;
  memSnapshot = data.snapshot || memSnapshot;
  document.getElementById('memBaselineBtn').classList.toggle('active', memHasBaseline);
  document.getElementById('memhint').textContent = memHasBaseline
    ? 'baseline marked — run your flow (push and pop the pages), then 📸 Snapshot: every type will show what it grew by, per repetition'
    : 'baseline cleared';
  renderMemoryTables();
  loadHistory();
}

function updateMemoryBadge(stats) {
  const badge = document.getElementById('membadge');
  const alive = stats.watch ? stats.watch.alive : 0;
  badge.hidden = alive === 0;
  badge.textContent = alive;
  badge.title = alive ? `${alive} popped page${alive === 1 ? '' : 's'} still alive` : '';
}

async function loadLedger() {
  try {
    memLedger = await (await fetch('/api/memory/ledger')).json();
  } catch {
    return;
  }
  renderLedger();
}

function renderLedger() {
  const host = document.getElementById('memledger');
  if (!memLedger || (memStats && memStats.tracking && memStats.tracking.enabled === false)) { host.innerHTML = ''; return; }
  const entries = memLedger.entries;
  const on = memSettings && memSettings.watch;
  let html = `<h3>Navigation ledger <span class="nethint">${entries.length} page${entries.length === 1 ? '' : 's'} · ${memLedger.alive} alive after pop · ${memLedger.pending} not judged yet`
    + (on ? ' · watch mode on' : ' · <b>watch mode off</b> — pending pages are judged by the next snapshot') + '</span></h3>';
  if (entries.length === 0) {
    host.innerHTML = html + '<div class="nethint">No page has entered a window since the inspector started. Navigate; every push and pop lands here with the memory it cost.</div>';
    return;
  }
  html += '<table class="memtable"><tr><th>Page</th><th>Pushed</th><th>Popped</th><th>Verdict</th><th class="num">Survived</th><th class="num">Δ managed</th><th class="num">Δ process</th></tr>';
  for (const e of entries.slice(0, 60)) {
    const verdict = e.verdict === 'alive' ? `<span class="verdict alive">✗ still alive</span>`
      : e.verdict === 'collected' ? '<span class="verdict ok">✓ collected</span>'
      : e.verdict === 'pending' ? '<span class="verdict pending">… pending</span>'
      : e.verdict === 'reattached' ? '<span class="verdict">↩ reattached</span>' : '<span class="verdict">open</span>';
    const origin = e.reported ? ' <span class="tag" title="Reported by the app through Immons.Tools.Maui.Inspector.Navigation — not a Page in a Window">reported</span>' : '';
    html += `<tr title="${pathEscape(e.type)}"><td class="tname">${pathEscape(e.label)}${origin}</td><td>${e.pushed}</td><td>${e.popped || ''}</td><td>${verdict}</td>`
      + `<td class="num">${e.verdict === 'alive' ? e.survived : ''}</td><td class="num">${e.managedDelta == null ? '' : signedBytes(e.managedDelta)}</td><td class="num">${e.processDelta == null ? '' : signedBytes(e.processDelta)}</td></tr>`;
  }
  host.innerHTML = html + '</table>';
}

function signedBytes(n) { return (n >= 0 ? '+' : '−') + fmtBytes(Math.abs(n)); }

function applyBaselineState(hasBaseline) {
  memHasBaseline = hasBaseline;
  document.getElementById('memBaselineBtn').classList.toggle('active', hasBaseline);
}

async function loadHistory() {
  try {
    memHistory = (await (await fetch('/api/memory/snapshots')).json()).snapshots || [];
  } catch {
    return;
  }
  drawHistory();
}

// Detached app objects per type across the snapshots — a line that keeps climbing is the leak.
function drawHistory() {
  const host = document.getElementById('memhistory');
  if (memHistory.length < 2) { host.innerHTML = ''; return; }
  const maxima = new Map();
  for (const s of memHistory) for (const [type, n] of Object.entries(s.types)) maxima.set(type, Math.max(maxima.get(type) || 0, n));
  const top = [...maxima.entries()].sort((a, b) => b[1] - a[1]).slice(0, 6).map(e => e[0]);
  const palette = ['#5c9eff', '#e8a33d', '#7fd48a', '#e08585', '#c9a0dc', '#9ba0ae'];
  host.innerHTML = `<h3>Snapshot history <span class="nethint">${memHistory.length} snapshots · detached objects per app type</span></h3>`
    + '<div class="histwrap"><canvas id="memhist" width="600" height="120"></canvas><div id="memhistlegend"></div></div>';
  const canvas = document.getElementById('memhist');
  const ctx = canvas.getContext('2d');
  const w = canvas.width, h = canvas.height;
  const maxY = Math.max(1, ...top.map(t => maxima.get(t)));
  const step = w / Math.max(memHistory.length - 1, 1);
  ctx.clearRect(0, 0, w, h);
  top.forEach((type, i) => {
    ctx.beginPath();
    memHistory.forEach((s, x) => {
      const y = h - 6 - ((s.types[type] || 0) / maxY) * (h - 16);
      x === 0 ? ctx.moveTo(0, y) : ctx.lineTo(x * step, y);
    });
    ctx.strokeStyle = palette[i]; ctx.lineWidth = 1.5; ctx.stroke();
  });
  ctx.fillStyle = '#9ba0ae'; ctx.font = '10px sans-serif'; ctx.fillText(String(maxY), 4, 10);
  document.getElementById('memhistlegend').innerHTML = top.map((t, i) => `<span style="color:${palette[i]}">■ ${pathEscape(t)} (${maxima.get(t)})</span>`).join(' ');
}

async function loadImages() {
  try {
    memImages = await (await fetch('/api/memory/images')).json();
  } catch {
    memImages = null;
  }
  renderImages();
}

function renderImages() {
  const host = document.getElementById('memimages');
  if (!memImages || !memImages.supported) { host.innerHTML = ''; return; }
  let html = `<h3>Images <span class="nethint">${memImages.total} decoded · ${fmtBytes(memImages.bytes)} · the bitmaps of the tracked Image / ImageButton elements${memStats && memStats.platform === 'android' ? ', plus every Bitmap peer the runtime still wraps' : ''}</span> <button onclick="loadImages()" title="Re-read">↻</button></h3>`;
  if (memImages.images.length === 0) { host.innerHTML = html + '<div class="nethint">No decoded image right now.</div>'; return; }
  html += '<table class="memtable"><tr><th>Shown by</th><th>Source</th><th class="num">Size</th><th class="num">Bytes</th><th>State</th></tr>';
  for (const i of memImages.images.slice(0, 60))
    html += `<tr class="${i.attached ? '' : 'fw'}"><td class="tname">${pathEscape(i.owner)}</td><td>${pathEscape(i.source)}</td><td class="num">${i.width}×${i.height}</td><td class="num">${fmtBytes(i.bytes)}</td><td>${i.attached ? 'in a window' : '<span class="delta-up">detached</span>'}</td></tr>`;
  host.innerHTML = html + '</table>';
}

// Exports: Markdown for a ticket, CSV for a spreadsheet. Downloads work because the panel is a plain page.
function downloadText(name, text, type) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([text], { type: type }));
  a.download = name;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

function exportMemoryMarkdown() {
  const lines = ['# Memory report — ' + (memStats ? (memStats.platform || '') : '') + ' — ' + new Date().toLocaleString(), ''];
  if (memStats) {
    const s = memStats.sample;
    lines.push(`Managed ${fmtBytes(s.managed)} · process ${fmtBytes(s.process)} · GC ${s.gen0}/${s.gen1}/${s.gen2} · tracked ${memStats.tracking.tracked}`, '');
  }
  if (memSnapshot) {
    lines.push(`## Snapshot ${memSnapshot.time}`, '', `${memSnapshot.totals.detached} detached of ${memSnapshot.totals.alive} alive`, '', '| Type | Role | Count | Survived | Holders / hints |', '|---|---|---|---|---|');
    for (const g of memSuspectGroups.values())
      lines.push(`| ${g.name} | ${g.kind} | ${g.count} | ${g.survived} | ${[...(g.holders || [])].concat([...g.hints]).join('; ')} |`);
    lines.push('');
  }
  if (memLedger && memLedger.entries.length) {
    lines.push('## Navigation ledger', '', '| Page | Pushed | Popped | Verdict | Δ managed |', '|---|---|---|---|---|');
    for (const e of memLedger.entries.slice(0, 40)) lines.push(`| ${e.label} | ${e.pushed} | ${e.popped || ''} | ${e.verdict} | ${e.managedDelta == null ? '' : signedBytes(e.managedDelta)} |`);
    lines.push('');
  }
  const dump = memDumps.find(j => j.phase === 'done' && j.report && j.report.kind !== 'trace');
  if (dump && dump.report.roots) {
    lines.push(`## Heap dump #${dump.id} — ${dump.report.totalObjects} objects, ${fmtBytes(dump.report.totalBytes)}`, '');
    for (const r of dump.report.roots) {
      lines.push(`### ${r.type} ×${r.matched}${r.retained ? ' · retained ' + fmtBytes(r.retained) : ''}`);
      for (const p of r.paths) lines.push('- ' + p.join(' ← '));
    }
  }
  downloadText('memory-report.md', lines.join('\n'), 'text/markdown');
}

function exportMemoryCsv() {
  const dump = memDumps.find(j => j.phase === 'done' && j.report && j.report.types);
  const rows = dump ? dump.report.types.map(t => [t.type, t.module || '', t.count, t.bytes, t.app]) : (memSnapshot ? memSnapshot.rows.map(r => [r.type, r.kind, r.alive, r.attached, r.detached, r.collected]) : []);
  const head = dump ? 'type,module,objects,bytes,app' : 'type,role,alive,attached,detached,collected';
  downloadText(dump ? 'heap-types.csv' : 'tracked-types.csv', [head, ...rows.map(r => r.map(v => '"' + String(v).replace(/"/g, '""') + '"').join(','))].join('\n'), 'text/csv');
}
