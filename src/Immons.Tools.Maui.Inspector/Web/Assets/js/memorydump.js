// Heap-dump hand-off: the panel orders a dump, maui-inspector-sync on the desktop runs
// dotnet-gcdump against the app (dsrouter for Android/iOS, the PID on Windows), reads the
// .gcdump and posts back a report — every type with counts and bytes, plus the paths from the
// snapshot's suspects to their GC roots. Needs the Diagnostics package on Android/iOS.
let memDumps = [];
let dumpTimer = null;
let dumpAnnounced = 0; // newest job id whose completion the hint already reported
let lastDumpsSignature = '';
// The types table: which module, which column, which way — kept across the re-renders the polling causes.
let dumpTable = { module: '', sort: 'bytes', dir: 'desc', changedOnly: false, inspector: false };

// The inspector's own trackers, reports and screenshot buffers are in the heap too. They are not
// what anyone opened this view for, so they are hidden until asked for.
function toggleInspectorObjects() { dumpTable.inspector = !dumpTable.inspector; renderDumps(); }

function hidesInspector(entry) { return !dumpTable.inspector && entry.inspector === true; }

// A finished report is a page of tables; three of them bury the view. Only the newest is open,
// and a new one takes the spotlight from the previous — anything the reader opened stays open.
const expandedDumps = new Set();
let newestDumpSeen = 0;

function trackNewestDump() {
  const newest = memDumps.length ? Math.max(...memDumps.map(j => j.id)) : 0;
  if (newest > newestDumpSeen) {
    expandedDumps.clear();
    expandedDumps.add(newest);
    newestDumpSeen = newest;
  }
}

function toggleDumpCard(id) {
  expandedDumps.has(id) ? expandedDumps.delete(id) : expandedDumps.add(id);
  renderDumps();
}

/// One line that says what a collapsed card holds.
function dumpSummary(job) {
  const r = job.report;
  if (!r) return '';
  if ((job.kind || 'dump') === 'alloc') return `${fmtBytes(r.totalBytes)} in ${r.seconds} s`;
  if (job.kind === 'trace') return `${(r.roots || []).reduce((n, x) => n + x.matched, 0)} instances`;
  return `${r.totalObjects} objects · ${fmtBytes(r.totalBytes)} · ${r.typeCount} types`;
}
const DUMP_ROWS_SHOWN = 400;

function setDumpSort(key) {
  if (dumpTable.sort === key) dumpTable.dir = dumpTable.dir === 'desc' ? 'asc' : 'desc';
  else { dumpTable.sort = key; dumpTable.dir = (key === 'type' || key === 'module') ? 'asc' : 'desc'; }
  renderDumps();
}

function setDumpModule(module) { dumpTable.module = module; renderDumps(); }
function toggleDumpChanged(on) { dumpTable.changedOnly = on; renderDumps(); }

// A module picked in the dropdown overrides "app types only" — picking SQLite-net with the box
// checked would otherwise show nothing. The text filter matches the type or the module.
function dumpRows(report, previousReport) {
  const filter = memFilter();
  const before = new Map((previousReport?.types || []).map(t => [t.type, t]));
  let rows = report.types.map(t => ({ ...t,
    delta: previousReport ? t.count - (before.get(t.type)?.count || 0) : null,
    bytesDelta: previousReport ? t.bytes - (before.get(t.type)?.bytes || 0) : null,
    isNew: previousReport ? !before.has(t.type) : false }));
  rows = rows.filter(t => !hidesInspector(t));
  if (dumpTable.module) rows = rows.filter(t => (t.module || '') === dumpTable.module);
  // Asking for the inspector's own objects outranks "app types only" — they are neither, and the
  // switch would otherwise look broken.
  else if (filter.appOnly) rows = rows.filter(t => t.app || (dumpTable.inspector && t.inspector));
  if (filter.text) rows = rows.filter(t => t.type.toLowerCase().includes(filter.text) || (t.module || '').toLowerCase().includes(filter.text));
  if (dumpTable.changedOnly && previousReport) rows = rows.filter(t => t.delta !== 0);
  const dir = dumpTable.dir === 'desc' ? -1 : 1;
  const key = dumpTable.sort;
  rows.sort((a, b) => {
    const pick = (t) => key === 'delta' ? (t.delta ?? 0) : key === 'bytesDelta' ? (t.bytesDelta ?? 0) : key === 'count' ? t.count : key === 'bytes' ? t.bytes : (t[key] || '');
    const av = pick(a), bv = pick(b);
    const c = typeof av === 'string' ? av.localeCompare(bv) : av - bv;
    return c !== 0 ? c * dir : b.bytes - a.bytes;
  });
  return rows;
}

function dumpModuleOptions(report) {
  const modules = new Map();
  for (const t of report.types) {
    const m = t.module || '(no module)';
    const entry = modules.get(m) || { app: false, count: 0 };
    entry.app = entry.app || t.app;
    entry.count++;
    modules.set(m, entry);
  }
  const sorted = [...modules.entries()].sort((a, b) => (b[1].app - a[1].app) || a[0].localeCompare(b[0]));
  return '<option value="">all modules</option>' + sorted.map(([m, e]) =>
    `<option value="${pathEscape(m)}"${dumpTable.module === m ? ' selected' : ''}>${e.app ? '★ ' : ''}${pathEscape(m)} (${e.count})</option>`).join('');
}

function dumpHeader(key, label, cls) {
  const active = dumpTable.sort === key;
  return `<th class="sortable${active ? ' active' : ''}${cls ? ' ' + cls : ''}" onclick="setDumpSort('${key}')" title="Sort by ${label.toLowerCase()}">${label}${active ? (dumpTable.dir === 'desc' ? ' ▾' : ' ▴') : ''}</th>`;
}

function updateDumpAvailability(stats) {
  const btn = document.getElementById('memDumpBtn');
  const reasons = [];
  if (!stats.diagnosticsAvailable)
    reasons.push('this build has no diagnostic port — add the Immons.Tools.Maui.Inspector.Diagnostics package (Debug builds) and rebuild');
  if (!stats.syncTool)
    reasons.push('maui-inspector-sync is not running — start it in the app\'s source folder (dotnet tool install -g Immons.Tools.Maui.Inspector.Sync); it installs dotnet-gcdump and dotnet-dsrouter itself on first use');
  btn.classList.toggle('unavailable', reasons.length > 0);
  const alloc = document.getElementById('memAllocBtn');
  alloc.classList.toggle('unavailable', reasons.length > 0 || stats.allocTracking === false);
  alloc.title = stats.allocTracking === false
    ? 'This app was started without Mono\'s allocation profiler (the Diagnostics package sets it for Debug builds by itself) — rebuild with an up-to-date package'
    : 'Record allocations for 10 s with dotnet-trace on the desktop — which types allocate how much while you use the app';
  btn.title = reasons.length
    ? 'Heap dump needs: ' + reasons.join('; ')
    : 'Whole managed heap through dotnet-gcdump on the desktop — every type with counts, sizes and paths to the GC roots';
}

async function requestHeapDump() {
  if (memStats && (!memStats.diagnosticsAvailable || !memStats.syncTool)) {
    document.getElementById('memhint').textContent = document.getElementById('memDumpBtn').title;
    return;
  }
  const data = await (await fetch('/api/memory/dump/request', { method: 'POST', body: JSON.stringify({}) })).json();
  const heap = memStats && memStats.sample ? fmtBytes(memStats.sample.managed) : '';
  document.getElementById('memhint').textContent = data.ok
    ? `heap dump #${data.job.id} requested — collecting ${heap} of managed heap takes about a minute per million objects`
    : 'request failed';
  startDumpPolling();
  loadDumps();
}

function startDumpPolling() {
  if (!dumpTimer) dumpTimer = setInterval(loadDumps, 1500);
}

async function cancelHeapDump(id) {
  await fetch('/api/memory/dump/cancel', { method: 'POST', body: JSON.stringify({ id: id }) });
  loadDumps();
}

// Reports are fetched once per job and kept here: the job list is polled, the reports are not.
const dumpReports = new Map();

async function loadDumps() {
  let jobs;
  try {
    jobs = (await (await fetch('/api/memory/dumps')).json()).jobs || [];
  } catch {
    return;
  }
  for (const job of jobs) {
    if (job.hasReport && !dumpReports.has(job.id)) {
      try {
        const fetched = (await (await fetch('/api/memory/dump/report?id=' + job.id)).json()).job;
        if (fetched && fetched.report) dumpReports.set(job.id, fetched.report);
      } catch { /* the app went away mid-fetch; the next poll retries */ }
    }
    job.report = dumpReports.get(job.id) || null;
  }
  memDumps = jobs;
  const active = memDumps.some(j => j.phase === 'pending' || j.phase === 'running');
  if (active) startDumpPolling();
  else if (dumpTimer) { clearInterval(dumpTimer); dumpTimer = null; }
  // Rebuilding this section throws away every node in it, including a <select> the user may have
  // open — so it happens only when something actually changed, not on every poll.
  const signature = memDumps.map(j => [j.id, j.phase, j.message, !!j.report].join(':')).join('|');
  if (signature === lastDumpsSignature) return;
  lastDumpsSignature = signature;
  const newest = memDumps[0];
  if (newest && !active && newest.id > dumpAnnounced) {
    dumpAnnounced = newest.id;
    document.getElementById('memhint').textContent = newest.phase === 'done'
      ? `heap dump #${newest.id} done — ${newest.report ? newest.report.totalObjects + ' objects, ' + newest.report.typeCount + ' types' : ''}`
      : `heap dump #${newest.id} failed: ${newest.message}`;
  }
  renderDumps();
}

// The app reports wall-clock times of its own device; the browser only needs a local start point.
const dumpStarts = new Map();

function jobStarted(job) {
  if (!dumpStarts.has(job.id)) dumpStarts.set(job.id, Date.now());
  return dumpStarts.get(job.id);
}

/// Ticks the "running 1m 42s" labels in place — a full re-render would throw away the section.
function tickDumpElapsed() {
  for (const span of document.querySelectorAll('#memdumps .dumpelapsed')) {
    const seconds = Math.round((Date.now() - Number(span.dataset.since)) / 1000);
    span.textContent = '· running ' + (seconds < 60 ? seconds + 's' : Math.floor(seconds / 60) + 'm ' + (seconds % 60) + 's');
  }
}

function renderDumps() {
  const host = document.getElementById('memdumps');
  if (!host) return;
  if (memDumps.length === 0) {
    host.innerHTML = '<h3>Heap dumps</h3><div class="nethint">None yet. 🧬 Heap dump hands the whole managed heap to dotnet-gcdump on the desktop: '
      + 'the report lists every type with counts and bytes, the difference to the previous dump, and — for the snapshot\'s suspects — who holds them (the path to the GC root).</div>';
    return;
  }
  trackNewestDump();
  let html = '<h3>Heap dumps <span class="nethint">dumps, traces of single types, allocation recordings · click a card to fold it</span></h3>';
  const done = memDumps.filter(j => j.phase === 'done' && (j.kind || 'dump') === 'dump');
  for (const job of memDumps) {
    const kind = job.kind || 'dump';
    const title = kind === 'trace' ? `trace of ${pathEscape((job.types || [])[0] || '')} in dump #${job.sourceJob}` : kind === 'alloc' ? `allocations · ${job.seconds} s` : 'heap dump';
    const held = job.reportBytes ? ` · <span class="nethint" title="What the inspector keeps in the app for this report (gzipped)">holding ${fmtBytes(job.reportBytes)}</span>` : '';
    const running = job.phase === 'pending' || job.phase === 'running'
      ? ` <span class="dumpelapsed" data-since="${jobStarted(job)}" title="Collecting streams every object out of the app, so it takes about a minute per million objects"></span>` : '';
    const open = expandedDumps.has(job.id);
    html += `<div class="dumpjob ${job.phase}${open ? '' : ' folded'}">`
      + `<div class="dumphead" onclick="toggleDumpCard(${job.id})" title="${open ? 'Fold' : 'Unfold'} this report">`
      + `<span class="caret">${open ? '▾' : '▸'}</span> <b>#${job.id}</b> <span class="nethint">${job.requested} · ${title}</span> · ${job.phase}${running}${held}`
      + (open ? '' : ` <span class="nethint">${dumpSummary(job)}</span>`) + '</div>';
    if (job.message) html += ` — ${pathEscape(job.message)}`;
    if (job.phase === 'pending' || job.phase === 'running')
      html += ` <button onclick="event.stopPropagation(); cancelHeapDump(${job.id})">✕ cancel</button>`;
    if (job.phase === 'pending') html += '<div class="memwarn">waiting for maui-inspector-sync to pick it up — is the tool running and connected to this app?</div>';
    if (open && job.phase === 'done' && job.report) {
      if (kind === 'alloc') html += renderAllocReport(job);
      else if (kind === 'trace') html += renderTraceReport(job);
      else {
        const previous = done[done.indexOf(job) + 1];
        html += renderDumpReport(job, previous ? previous.report : null);
      }
    }
    html += '</div>';
  }
  host.innerHTML = html;
}

function renderDumpReport(job, previousReport) {
  const r = job.report;
  const mine = (r.types || []).filter(t => t.inspector);
  const mineBytes = r.inspectorBytes != null ? r.inspectorBytes : mine.reduce((sum, t) => sum + t.bytes, 0);
  let html = `<div class="nethint">${r.totalObjects} objects · ${fmtBytes(r.totalBytes)} · ${r.typeCount} types · ${r.tool}`
    + (job.file ? ` · <span class="tname" title="Open in Visual Studio / PerfView on Windows">${job.file}</span>` : '')
    + (mine.length
        ? ` · <button class="tracebtn" onclick="toggleInspectorObjects()" title="The inspector's own trackers, reports and screenshot buffers">`
          + `${dumpTable.inspector ? '◉' : '○'} inspector's own (${fmtBytes(mineBytes)})</button>`
        : '')
    + '</div>';

  if (r.roots && r.roots.length) {
    const appTypes = reportAppTypes(r);
    pathPackageTypes = reportPackageTypes(r);
    const filter = memFilter();
    // The report says who owns each traced type; only a report from an older tool needs the fallback.
    const roots = r.roots.filter(root => !hidesInspector(root))
      .filter(root => !filter.appOnly || root.app !== false || appTypes.has(root.type) || root.matched === 0);
    html += `<h4>Who holds the suspects <span class="nethint">shortest chain to a GC root · click one to see it as a stack${roots.length < r.roots.length ? ` · ${r.roots.length - roots.length} framework type(s) hidden` : ''}</span></h4>`;
    roots.forEach((root, rootIndex) => {
      html += `<div class="suspect"><span class="stype">${pathEscape(root.type)}</span><span class="skind">${root.matched} instance${root.matched === 1 ? '' : 's'} in the dump${root.retained ? ` · <b title="Bytes that would go away with these instances">retained ${fmtBytes(root.retained)}</b>` : ''}</span>`;
      if (root.paths.length === 0) html += '<div class="rootpath static">unreachable from a root — garbage that was not collected yet</div>';
      root.paths.forEach((path, pathIndex) => {
        const kind = rootKind(path);
        html += renderPathPreview(job.id, r.roots.indexOf(root), pathIndex, path, appTypes, kind);
      });
      html += '</div>';
    });
  }

  if (r.largest && r.largest.length) {
    html += '<h4>Largest objects <span class="nethint">single objects by size — arrays behind images, caches, buffers; click a path for the chain</span></h4>';
    const appTypes = reportAppTypes(r);
    pathPackageTypes = reportPackageTypes(r);
    r.largest.filter(o => !hidesInspector(o)).slice(0, 15).forEach((o, i) => {
      html += `<div class="suspect${o.app ? '' : ' fw'}"><span class="stype">${pathEscape(o.type)}</span><span class="skind">${fmtBytes(o.bytes)}${o.retained > o.bytes ? ' · retained ' + fmtBytes(o.retained) : ''}</span>`
        + `<div class="rootpath" onclick="openLargestPopup(${job.id}, ${i})" title="Click for the whole chain as a stack">${pathPreviewSteps(o.path).map((t, k) => `<span class="${t.startsWith('… ') ? 'pstep gap' : stepClass(t, k, appTypes)}">${pathEscape(t)}</span>`).join('<span class="parrow">←</span>')}</div></div>`;
    });
  }

  const rows = dumpRows(r, previousReport);
  const previousId = previousReport ? memDumps.find(j => j.report === previousReport)?.id : null;
  html += `<h4>Types <span class="nethint">${rows.length} shown${rows.length > DUMP_ROWS_SHOWN ? ` (first ${DUMP_ROWS_SHOWN})` : ''} · Δ = objects vs dump #${previousId ?? '–'}</span>`
    + `<span class="dumpbar"><select onchange="setDumpModule(this.value)" title="Only this module (overrides “app types only”)">${dumpModuleOptions(r)}</select>`
    + (previousReport ? `<label class="cooklabel"><input type="checkbox" onchange="toggleDumpChanged(this.checked)"${dumpTable.changedOnly ? ' checked' : ''}> Δ only</label>` : '')
    + '</span></h4>';
  html += `<table class="memtable"><tr>${dumpHeader('type', 'Type')}${dumpHeader('module', 'Module')}${dumpHeader('count', 'Objects', 'num')}${dumpHeader('delta', 'Δ', 'num')}${dumpHeader('bytes', 'Bytes', 'num')}${dumpHeader('bytesDelta', 'Δ bytes', 'num')}<th></th></tr>`;
  for (const t of rows.slice(0, DUMP_ROWS_SHOWN)) {
    html += `<tr class="${typeRowClass(t)}" title="${pathEscape(t.type)}"><td class="tname">${pathEscape(t.type)}${t.isNew ? ' <span class="newtype" title="Not in the previous dump">new</span>' : ''}</td><td>${pathEscape(t.module || '')}</td><td class="num">${t.count}</td>`
      + deltaCell(t.delta) + `<td class="num">${fmtBytes(t.bytes)}</td>${bytesDeltaCell(t.bytesDelta)}`
      + `<td><button class="tracebtn" onclick="requestTrace(${job.id}, '${pathEscape(t.type).replace(/'/g, "\\'")}')" title="Trace this type to its GC roots (reads the dump on the desktop)">🧬</button></td></tr>`;
  }
  return html + '</table>';
}

function bytesDeltaCell(d) {
  if (d == null || d === 0) return '<td class="num"></td>';
  return `<td class="num ${d > 0 ? 'delta-up' : 'delta-down'}">${d > 0 ? '+' : '−'}${fmtBytes(Math.abs(d))}</td>`;
}

async function requestTrace(jobId, type) {
  const data = await (await fetch('/api/memory/dump/trace', { method: 'POST', body: JSON.stringify({ jobId: jobId, type: type }) })).json();
  document.getElementById('memhint').textContent = data.ok ? `trace #${data.job.id} of ${type} requested` : 'trace request failed';
  startDumpPolling();
  loadDumps();
}

async function requestAllocRecording(seconds) {
  if (memStats && (!memStats.diagnosticsAvailable || !memStats.syncTool)) {
    document.getElementById('memhint').textContent = document.getElementById('memDumpBtn').title;
    return;
  }
  if (memStats && memStats.allocTracking === false) {
    document.getElementById('memhint').textContent = 'This app was started without Mono\'s allocation profiler, which cannot be turned on later. '
      + 'The Diagnostics package sets it for Debug builds by itself — update it (or drop <MauiInspectorAllocationTracking>false</MauiInspectorAllocationTracking>) and rebuild.'
      + (memStats.platform === 'android' ? ' Without a rebuild: adb shell setprop debug.mono.env "MONO_DIAGNOSTICS=--diagnostic-mono-profiler=alloc", then restart the app.' : '');
    return;
  }
  const data = await (await fetch('/api/memory/alloc/request', { method: 'POST', body: JSON.stringify({ seconds: seconds }) })).json();
  document.getElementById('memhint').textContent = data.ok ? `allocation recording #${data.job.id}: ${seconds} s — use the app now` : 'request failed';
  startDumpPolling();
  loadDumps();
}

// Allocations: the toolbar's "app types only" applies here too, but on its own terms — the app's
// code mostly allocates framework types, so hiding them is a deliberate act, and the row above the
// table says how many were hidden and offers them back in one click.
let allocAppOnly = false;

function toggleAllocScope() {
  allocAppOnly = !allocAppOnly;
  renderDumps();
}

function renderAllocReport(job) {
  const r = job.report;
  const filter = memFilter();
  const live = liveCounts();
  const byText = r.types.filter(t => !filter.text || t.type.toLowerCase().includes(filter.text));
  const visible = byText.filter(t => !hidesInspector(t));
  const rows = allocAppOnly ? visible.filter(t => t.app) : visible;
  const hidden = byText.length - rows.length;
  const how = r.sampled === false ? `${r.samples} allocations (every one, Mono's profiler)` : `${r.samples} ticks (one per ~100 KB allocated)`;
  const appBytes = r.types.filter(t => t.app).reduce((sum, t) => sum + t.bytes, 0);
  let html = `<div class="nethint">${r.seconds} s · ${fmtBytes(r.totalBytes)} in ${how} · ${fmtBytes(appBytes)} of it in the app's own types · ${r.tool} · <span class="tname">${pathEscape(r.file || '')}</span></div>`;
  html += `<div class="allocbar"><button onclick="toggleAllocScope()" class="${allocAppOnly ? 'active' : ''}" title="Only types from the app's own assemblies — its packages and the framework are hidden">`
    + `${allocAppOnly ? '◉' : '○'} app types only</button>`
    + (allocAppOnly && hidden ? `<span class="nethint">${hidden} framework / package type${hidden === 1 ? '' : 's'} hidden</span>` : '')
    + (live.id ? `<span class="nethint">Live = objects of that type in heap dump #${live.id}</span>` : '<span class="nethint">take a 🧬 heap dump to see how many of each type are alive</span>')
    + '</div>';
  html += `<table class="memtable"><tr><th>Type</th><th class="num">Bytes</th><th class="num">per second</th><th class="num">${r.sampled === false ? 'Allocated' : 'Samples'}</th>`
    + (live.id ? '<th class="num">Live</th><th></th>' : '') + '</tr>';
  for (const t of rows.slice(0, 200)) {
    const alive = live.counts ? live.counts.get(t.type) : undefined;
    html += `<tr class="${typeRowClass(t)}" title="${pathEscape(t.type)}"><td class="tname">${pathEscape(t.type)}</td><td class="num">${fmtBytes(t.bytes)}</td>`
      + `<td class="num">${fmtBytes(t.bytes / Math.max(r.seconds, 1))}/s</td><td class="num">${t.samples}</td>`
      + (live.id
        ? `<td class="num">${alive == null ? '' : alive}</td><td>${alive ? `<button class="tracebtn" onclick="requestTrace(${live.id}, '${pathEscape(t.type).replace(/'/g, "\\'")}')" title="Who holds these — trace the type to its GC roots in dump #${live.id}">🧬</button>` : ''}</td>`
        : '') + '</tr>';
  }
  return html + '</table>';
}

/// The newest finished heap dump, as a type → live-object-count map.
function liveCounts() {
  const dump = memDumps.find(j => j.phase === 'done' && j.report && (j.kind || 'dump') === 'dump');
  if (!dump) return {};
  return { id: dump.id, counts: new Map(dump.report.types.map(t => [t.type, t.count])) };
}

/// App types read as app types, packages as packages, the framework stays quiet.
function typeRowClass(t) {
  return t.app ? 'apptype' : t.package ? 'pkgtype' : 'fw';
}

function renderTraceReport(job) {
  const r = job.report;
  const appTypes = reportAppTypes(memDumps.find(j => j.id === r.sourceJob)?.report || { types: [] });
  let html = `<div class="nethint">root paths in dump #${r.sourceJob} · ${r.tool}</div>`;
  r.roots.forEach((root, rootIndex) => {
    html += `<div class="suspect"><span class="stype">${pathEscape(root.type)}</span><span class="skind">${root.matched} instance${root.matched === 1 ? '' : 's'}${root.retained ? ' · retained ' + fmtBytes(root.retained) : ''}</span>`;
    if (root.paths.length === 0) html += '<div class="rootpath static">no instance in the dump, or unreachable</div>';
    root.paths.forEach((path, pathIndex) => { html += renderPathPreview(job.id, rootIndex, pathIndex, path, appTypes); });
    html += '</div>';
  });
  return html;
}
