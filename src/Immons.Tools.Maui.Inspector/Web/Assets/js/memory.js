// Memory view: live readings (sparkline), leak snapshots — what survived several full collections
// although no window uses it any more — and the platform's peer census. The heap-dump hand-off
// (dotnet-gcdump through maui-inspector-sync) lives in memorydump.js.
let memTimer = null;
let memStats = null;
let memSnapshot = null;
let memSnapshotSeq = -1;
let memPeers = null;
let memPeersAll = false;
let memSuspectGroups = new Map(); // group key → aggregated suspect group, for the parents popup

let memTicks = 0;

// The live readings are computed per request — nothing samples in the app between polls. One
// reading costs about 5 ms on a background thread (measured on an Android emulator: /api/memory
// 17.5 ms per call against 12.8 ms for the trivial /api/ping), and none of it touches the UI
// thread. Still: with tracking off there is nothing to catch quickly, so the panel asks five times
// less often, and a panel left open in a background browser tab stops asking at all.
const MEM_POLL_MS = 1000;
const MEM_POLL_IDLE_MS = 5000;

function memPollInterval() {
  return memTrackingOff() ? MEM_POLL_IDLE_MS : MEM_POLL_MS;
}

function restartMemoryPolling() {
  if (memTimer) { clearInterval(memTimer); memTimer = null; }
  if (activeView !== 'memory' || document.hidden) return;
  memTimer = setInterval(pollMemory, memPollInterval());
}

document.addEventListener('visibilitychange', () => {
  if (activeView !== 'memory') return;
  restartMemoryPolling();
  if (!document.hidden) pollMemory();
});

async function showMemory() {
  restartMemoryPolling();
  // Awaited: the first poll is what tells us whether tracking is on at all.
  await pollMemory();
  loadPeers();
  loadDumps();
  if (memTrackingOff()) return;
  if (!memSnapshot) loadSnapshot();
  loadLedger();
  loadHistory();
  loadImages();
}

function stopMemory() {
  if (memTimer) { clearInterval(memTimer); memTimer = null; }
}

async function pollMemory() {
  try {
    memStats = await (await fetch('/api/memory')).json();
  } catch {
    return; // connection.js reports the outage
  }
  renderMemoryNumbers(memStats);
  drawSparkline(memStats.samples || [], memStats.events || []);
  updateDumpAvailability(memStats);
  applyMemorySettings(memStats.settings || {});
  updateMemoryBadge(memStats);
  // Watch mode takes snapshots in the app; the panel only learns about them from this counter.
  if (memStats.snapshot && memStats.snapshot.seq !== memSnapshotSeq) {
    memSnapshotSeq = memStats.snapshot.seq;
    loadSnapshot();
    loadHistory();
  }
  tickDumpElapsed();
  // The ledger changes with navigation, not with time — every few ticks is plenty. With tracking
  // off there is nothing to ask for, so the panel stops asking.
  if (++memTicks % 3 === 0 && !memTrackingOff()) loadLedger();
}

function fmtBytes(n) {
  if (n == null) return '–';
  if (n >= 1024 * 1024 * 1024) return (n / 1024 / 1024 / 1024).toFixed(2) + ' GB';
  if (n >= 1024 * 1024) return (n / 1024 / 1024).toFixed(1) + ' MB';
  if (n >= 1024) return (n / 1024).toFixed(0) + ' KB';
  return n + ' B';
}

function fmtAge(ms) {
  const s = Math.round(ms / 1000);
  return s < 60 ? s + 's' : Math.floor(s / 60) + 'm ' + (s % 60) + 's';
}

function renderMemoryNumbers(stats) {
  const s = stats.sample;
  const parts = [
    // Which process this is: two apps on one simulator take neighbouring panel ports, and measuring
    // the wrong one looks exactly like a fixed leak.
    `<b>${pathEscape(stats.app || 'app')}</b> <span title="process id">pid ${stats.pid}</span>`,
    `<b>Managed</b> ${fmtBytes(s.managed)}`,
    `<b>GC</b> ${s.gen0} / ${s.gen1} / ${s.gen2}`,
    `<b>allocated</b> ${fmtBytes(s.allocated)}`,
  ];
  if (s.process != null) parts.push(`<b>Process</b> ${fmtBytes(s.process)}`);
  if (s.javaHeap != null) parts.push(`<b>Java heap</b> ${fmtBytes(s.javaHeap)}`);
  if (s.nativeHeap != null) parts.push(`<b>native heap</b> ${fmtBytes(s.nativeHeap)}`);
  if (s.grefs != null) parts.push(`<b>GREF</b> ${s.grefs} <span title="weak global references">(+${s.weakGrefs} weak)</span>`);
  if (s.pss != null) parts.push(`<b>PSS</b> ${fmtBytes(s.pss)}`);
  if (s.graphics != null) parts.push(`<b>graphics</b> ${fmtBytes(s.graphics)}`);
  if (s.available != null) parts.push(`<b>headroom</b> ${fmtBytes(s.available)} <span title="what iOS still lets this process allocate before it is killed">before jetsam</span>`);
  const tracking = stats.tracking.enabled
    ? `<b>Tracked</b> ${stats.tracking.tracked} objects`
    : '<b>Tracking off</b> — no snapshots, no ledger, no per-element work; these readings are polled, not sampled';
  document.getElementById('memnumbers').innerHTML = parts.join(' · ') + '<br>' + tracking;
}

// Managed heap (accent) and process memory (amber), each scaled to its own maximum —
// the shapes are what matter: a staircase that never comes down is the leak.
function drawSparkline(samples, events) {
  const canvas = document.getElementById('memspark');
  const ctx = canvas.getContext('2d');
  const w = canvas.width, h = canvas.height;
  ctx.clearRect(0, 0, w, h);
  const step0 = w / Math.max(samples.length - 1, 1);
  // Markers: a gen-2 collection (thin), an OS memory warning / trim (red), timed by the sample clock.
  samples.forEach((s, i) => {
    if (i > 0 && s.gen2 > samples[i - 1].gen2) { ctx.fillStyle = 'rgba(155,160,174,.35)'; ctx.fillRect(i * step0, 0, 1, h); }
  });
  const times = samples.map(s => s.time);
  for (const e of events || []) {
    const i = times.indexOf(e.time);
    if (i < 0) continue;
    ctx.fillStyle = e.kind === 'warning' || e.kind === 'low' ? '#e05252' : '#e8a33d';
    ctx.fillRect(i * step0 - 1, 0, 2, h);
  }
  const series = [
    { key: 'managed', color: '#5c9eff', fill: 'rgba(92,158,255,.18)' },
    { key: 'process', color: '#e8a33d', fill: null },
  ];
  for (const serie of series) {
    const values = samples.map(s => s[serie.key]).filter(v => v != null);
    if (values.length < 2) continue;
    const max = Math.max(...values) || 1;
    const step = w / Math.max(samples.length - 1, 1);
    ctx.beginPath();
    samples.forEach((s, i) => {
      const v = s[serie.key];
      if (v == null) return;
      const x = i * step, y = h - 4 - (v / max) * (h - 8);
      i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
    });
    ctx.strokeStyle = serie.color;
    ctx.lineWidth = 1.5;
    ctx.stroke();
    if (serie.fill) {
      ctx.lineTo((samples.length - 1) * step, h); ctx.lineTo(0, h); ctx.closePath();
      ctx.fillStyle = serie.fill; ctx.fill();
    }
    ctx.fillStyle = serie.color;
    ctx.font = '10px sans-serif';
    ctx.fillText(fmtBytes(max), 4, serie.key === 'managed' ? 10 : 22);
  }
}

async function forceGc() {
  const data = await (await fetch('/api/memory/gc', { method: 'POST' })).json();
  document.getElementById('memhint').textContent = 'after GC: managed ' + fmtBytes(data.sample.managed);
  pollMemory();
}

async function loadSnapshot() {
  const data = await (await fetch('/api/memory/snapshot')).json();
  memSnapshot = data.snapshot;
  applyBaselineState(!!data.hasBaseline);
  renderMemoryTables();
}

async function runSnapshot() {
  if (typeof memTrackingOff === 'function' && memTrackingOff()) return;
  const btn = document.getElementById('memSnapBtn');
  btn.disabled = true;
  document.getElementById('memhint').textContent = 'collecting…';
  try {
    const data = await (await fetch('/api/memory/snapshot', { method: 'POST' })).json();
    memSnapshot = data.snapshot;
    applyBaselineState(!!data.hasBaseline);
    if (memStats && memStats.snapshot) memSnapshotSeq = memStats.snapshot.seq + 1;
    document.getElementById('memhint').textContent = memSnapshot
      ? `snapshot ${memSnapshot.time}: ${memSnapshot.rounds} GC rounds in ${memSnapshot.elapsedMs} ms`
      : '';
  } finally {
    btn.disabled = false;
  }
  renderMemoryTables();
  pollMemory();
  loadLedger();
  loadHistory();
  loadImages();
}

function memFilter() {
  return { appOnly: document.getElementById('memAppOnly').checked,
           role: document.getElementById('memRole').value,
           text: document.getElementById('memsearch').value.trim().toLowerCase() };
}

function memMatches(row, filter) {
  return (!filter.appOnly || row.app) && (!filter.role || !row.kind || row.kind === filter.role)
    && (!filter.text || row.type.toLowerCase().includes(filter.text) || (row.module || '').toLowerCase().includes(filter.text));
}

function renderMemoryTables() {
  renderSuspects();
  renderSnapshotRows();
  renderPeers();
  renderDumps();
}

// Something detached seconds ago may simply be between screens — but hiding it was worse: watch
// mode snapshots right after navigation, so a real leak is always "fresh" the first time it is
// seen, and the list came up empty under a header saying 7 detached. Everything is listed; the
// young ones are marked and sorted last.
function isFresh(group) { return group.survived <= 1 && group.ageMs < 30000; }

// Suspects grouped by type and role: one line per group with the strongest evidence.
function renderSuspects() {
  const host = document.getElementById('memsuspects');
  if (memStats && memStats.tracking && memStats.tracking.enabled === false) {
    host.innerHTML = '<h3>Leak suspects</h3><div class="nethint">Tracking is off — the inspector records nothing and holds nothing of this app. '
      + 'The readings above still update because they are read on request, once per poll, on a background thread — about 5 ms each, '
      + 'nothing between polls and nothing at all once you leave this tab. Turn ⏻ Tracking back on to look for leaks again.</div>';
    return;
  }
  if (!memSnapshot) {
    host.innerHTML = '<h3>Leak suspects</h3><div class="nethint">No snapshot yet — navigate around the app (push a page, go back, repeat), then press 📸 Snapshot. '
      + 'Whatever is still alive without a window is listed here; take another snapshot after more navigation and watch the counts.</div>';
    return;
  }
  const filter = memFilter();
  const groups = new Map();
  for (const s of memSnapshot.suspects) {
    if (!memMatches(s, filter)) continue;

    const key = s.type + '|' + s.kind;
    const g = groups.get(key) || { ...s, key: key, count: 0, survived: 0, ageMs: 0, hints: new Set(), owners: new Set(), chains: new Map(), holders: new Set() };
    g.count++;
    g.survived = Math.max(g.survived, s.survived);
    g.ageMs = Math.max(g.ageMs, s.ageMs);
    s.hints.forEach(h => g.hints.add(h));
    (s.holders || []).forEach(h => g.holders.add(h));
    if (s.owner) g.owners.add(s.owner);
    if (s.parents && s.parents.length) {
      const chainKey = s.parents.join('|');
      g.chains.set(chainKey, (g.chains.get(chainKey) || 0) + 1);
    }
    groups.set(key, g);
  }
  memSuspectGroups = groups;
  const t = memSnapshot.totals;
  const base = memSnapshot.baseline;
  const collected = `${t.collected} collected since the last snapshot` + (t.collectedTotal > t.collected ? `, ${t.collectedTotal} since the baseline` : '');
  const growth = base
    ? ` · <b class="${base.grew > 0 ? 'delta-up' : 'delta-down'}">${base.grew >= 0 ? '+' : ''}${base.grew} objects vs the baseline (${base.time})</b>`
      + (base.cycles > 0 ? ` over ${base.cycles} navigation${base.cycles === 1 ? '' : 's'} — <b>${(base.grew / base.cycles).toFixed(1)} per repetition</b>` : ' — navigate the flow, then snapshot again')
    : '';
  // What the suspects cost, when a heap dump is there to measure it.
  const held = [...groups.values()].map(g => retainedFor(g.name)).filter(Boolean);
  const heldBytes = held.reduce((sum, r) => sum + r.bytes, 0);
  const cost = held.length
    ? ` · <b title="Retained size from heap dump #${held[0].jobId}: the bytes that would go away with these objects">holds ${fmtBytes(heldBytes)}</b>`
    : (memDumps.some(j => j.phase === 'done' && j.report?.roots?.length)
        ? ''
        : ' · <span title="A heap dump measures what these objects hold">🧬 heap dump to measure the cost</span>');
  const head = `<h3>Leak suspects <span class="nethint">${t.detached} detached of ${t.alive} alive · ${collected} · ${memSnapshot.time}${growth}${cost}</span></h3>`;
  if (groups.size === 0) {
    host.innerHTML = head + '<div class="nethint">Nothing that outlived its screen'
      + (filter.appOnly ? ' among app types — clear “app types only” to see the rest' : '')
      + '.</div>';
    return;
  }
  let html = head;
  const ordered = [...groups.values()].sort((a, b) =>
    (isFresh(a) - isFresh(b)) || (b.app - a.app) || (b.survived - a.survived) || (b.count - a.count));
  for (const g of ordered) {
    const hints = [...g.hints].map(h => `<span class="shint">${h}</span>`).join('');
    const owners = g.owners.size ? `<span class="skind">of ${[...g.owners].join(', ')}</span>` : '';
    const young = isFresh(g)
      ? '<span class="tag" title="Detached moments ago and seen once — often state between screens; watch whether it survives the next snapshots">just detached</span>'
      : '';
    const retained = retainedFor(g.name);
    const cost = retained ? `<span class="scost" title="Retained size in heap dump #${retained.jobId} — what would go away with these ${retained.matched} instances">holds ${fmtBytes(retained.bytes)}</span>` : '';
    const advice = leakAdvice(g);
    const fix = advice.length
      ? `<details class="sfix"><summary>💡 how to fix</summary>${advice.map(a => `<div class="sfixline">${a}</div>`).join('')}</details>`
      : '';
    const held = [...g.holders].map(h => `<div class="sholder">⛓ held by ${pathEscape(h)}</div>`).join('');
    const clickable = g.chains.size > 0 || g.holders.size > 0;
    const open = clickable ? ` onclick="if (!event.target.closest('details')) openParentsPopup('${g.key.replace(/'/g, "\\'")}')"` : '';
    const where = clickable ? `<span class="skind swhere" title="Click for the chain of parents up to the oldest ancestor">⇡ ${g.chains.size} parent chain${g.chains.size === 1 ? '' : 's'}</span>` : '';
    html += `<div class="suspect${g.app ? '' : ' fw'}${clickable ? ' clickable' : ''}" title="${g.type}"${open}><span class="stype">${g.name}</span> ×${g.count}`
      + `${young}<span class="skind">${g.kind}</span>${owners}<span class="skind">survived ${g.survived} snapshot${g.survived === 1 ? '' : 's'} · ${fmtAge(g.ageMs)}</span>${cost}${hints}${where}${held}${fix}</div>`;
  }
  html += `<div class="nethint sfooter">${LEAK_ADVICE_FOOTER}</div>`;
  host.innerHTML = html;
}

function deltaCell(d) {
  if (d == null || d === 0) return '<td class="num"></td>';
  return `<td class="num ${d > 0 ? 'delta-up' : 'delta-down'}">${d > 0 ? '+' : ''}${d}</td>`;
}

function renderSnapshotRows() {
  const host = document.getElementById('memrows');
  if (!memSnapshot) { host.innerHTML = ''; return; }
  const filter = memFilter();
  const rows = memSnapshot.rows.filter(r => memMatches(r, filter) && (r.alive > 0 || r.collected > 0));
  const hasBase = !!memSnapshot.baseline;
  const cycles = memSnapshot.baseline ? memSnapshot.baseline.cycles : 0;
  let html = `<h3>Tracked instances by type <span class="nethint">${rows.length} types · Δ = alive vs the previous snapshot${hasBase ? ' · Δ base = vs the baseline' + (cycles ? ', /rep = per repetition' : '') : ''}</span></h3>`;
  html += `<table class="memtable"><tr><th>Type</th><th>Role</th><th class="num">Alive</th><th class="num">In a window</th><th class="num">Detached</th><th class="num">Δ</th>`
    + (hasBase ? '<th class="num">Δ base</th>' + (cycles ? '<th class="num">/rep</th>' : '') : '') + '<th class="num">Collected</th></tr>';
  for (const r of rows) {
    html += `<tr class="${r.app ? '' : 'fw'}" title="${r.type}"><td class="tname">${r.name}</td><td>${r.kind}</td><td class="num">${r.alive}</td>`
      + `<td class="num">${r.attached}</td><td class="num${r.detached ? ' delta-up' : ''}">${r.detached || ''}</td>${deltaCell(r.delta)}`
      + (hasBase ? deltaCell(r.baseDelta) + (cycles ? `<td class="num${r.baseDelta > 0 ? ' delta-up' : ''}">${r.baseDelta ? (r.baseDelta / cycles).toFixed(1) : ''}</td>` : '') : '')
      + `<td class="num" title="${r.collectedTotal || 0} since the baseline">${r.collected || ''}</td></tr>`;
  }
  host.innerHTML = html + '</table>';
}

async function loadPeers() {
  try {
    memPeers = await (await fetch('/api/memory/peers')).json();
  } catch {
    memPeers = null;
  }
  renderPeers();
}

// Android only: every surfaced Java peer, tracker or not — the view that catches leaked platform views.
function renderPeers() {
  const host = document.getElementById('mempeers');
  if (!memPeers || !memPeers.supported) { host.innerHTML = ''; return; }
  const filter = memFilter();
  const all = memPeers.types.filter(t => memMatches(t, filter));
  const shown = memPeersAll ? all : all.slice(0, 40);
  let html = `<h3>Java peers <span class="nethint">${memPeers.total} surfaced · GREF ${memPeers.grefs} (+${memPeers.weakGrefs} weak) · Java.Interop's own list, independent of the tracker</span>`
    + ` <button onclick="loadPeers()" title="Re-read">↻</button></h3>`;
  html += '<table class="memtable"><tr><th>Type</th><th class="num">Peers</th></tr>';
  for (const t of shown)
    html += `<tr class="${t.app ? '' : 'fw'}" title="${t.type}"><td class="tname">${t.name}</td><td class="num">${t.count}</td></tr>`;
  html += '</table>';
  if (all.length > shown.length)
    html += `<button onclick="memPeersAll = true; renderPeers()">show all ${all.length}</button>`;
  host.innerHTML = html;
}
