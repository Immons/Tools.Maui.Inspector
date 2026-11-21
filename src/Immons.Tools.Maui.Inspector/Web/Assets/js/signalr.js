// SignalR view: hubs registered with WithMauiInspector(name) — live state, reconnects and the
// message timeline in both directions. Events are fetched incrementally (?since=) and kept in a
// bounded client cache; filters apply locally so switching them never refetches.
const SR_CLIENT_LIMIT = 5000;   // events kept in the browser across all hubs
const SR_RENDER_LIMIT = 1000;   // rows in the DOM

const srUi = {
  lastSeq: 0, events: [], hubs: [], paused: false, loading: false,
  renderedKey: null, expandedSeq: null, injectOpen: false, hubsKey: '',
};

async function loadSignalR() {
  if (srUi.loading) return;
  srUi.loading = true;
  try {
    const state = await (await fetch('/api/signalr')).json();
    // The app restarted (sequence went backwards) — start over.
    if (state.lastSeq < srUi.lastSeq) { srUi.events = []; srUi.lastSeq = 0; srUi.renderedKey = null; }
    let fresh = [];
    if (state.lastSeq > srUi.lastSeq) {
      const r = await (await fetch('/api/signalr/events?since=' + srUi.lastSeq + '&limit=' + SR_CLIENT_LIMIT)).json();
      fresh = r.events || [];
      srUi.lastSeq = Math.max(srUi.lastSeq, r.lastSeq || 0);
      srUi.events.push(...fresh);
      if (srUi.events.length > SR_CLIENT_LIMIT) srUi.events.splice(0, srUi.events.length - SR_CLIENT_LIMIT);
    }
    srUi.hubs = state.hubs || [];
    srUi.paused = !!state.paused;
    renderSrBar();
    renderSrHubs();
    renderSrEvents(fresh);
    if (srUi.injectOpen) refreshInjectHubs();
    updateSrBadge(srUi.hubs);
  } catch { /* connection state is shown in the header */ }
  finally { srUi.loading = false; }
}

// ── Toolbar ──────────────────────────────────────────────────────────────────
function renderSrBar() {
  const pick = document.getElementById('srhubfilter');
  const names = srUi.hubs.map(h => h.name);
  const have = [...pick.options].slice(1).map(o => o.value);
  if (names.join('\u0000') !== have.join('\u0000')) {
    const current = pick.value;
    pick.length = 1;
    for (const n of names) pick.add(new Option(n, n));
    pick.value = names.includes(current) ? current : '';
  }
  const pause = document.getElementById('srPauseBtn');
  pause.textContent = srUi.paused ? '▶ Resume' : '⏸ Pause';
  pause.classList.toggle('active', srUi.paused);
  const hint = document.getElementById('srhint');
  hint.textContent = srUi.paused
    ? 'Paused — messages are not recorded; state changes and counters still are.'
    : (srUi.hubs.length ? '' : '');
}

function srFilterChanged() {
  srUi.renderedKey = null;
  renderSrEvents([]);
}

async function srTogglePause() {
  const r = await (await fetch('/api/signalr/pause', { method: 'POST', body: JSON.stringify({ on: !srUi.paused }) })).json();
  srUi.paused = !!r.paused;
  renderSrBar();
}

async function srClear() {
  const hub = document.getElementById('srhubfilter').value;
  await fetch('/api/signalr/clear', { method: 'POST', body: JSON.stringify({ hub }) });
  srUi.events = hub ? srUi.events.filter(e => e.hub !== hub) : [];
  srUi.expandedSeq = null;
  srUi.renderedKey = null;
  loadSignalR();
}

function updateSrBadge(hubs) {
  const badge = document.getElementById('srbadge');
  if (!badge) return;
  const unhealthy = hubs.filter(h => h.state === 'Reconnecting' || (h.state === 'Disconnected' && h.lastError)).length;
  badge.hidden = unhealthy === 0;
  badge.textContent = unhealthy;
  badge.title = unhealthy + ' hub(s) reconnecting or dropped';
}

// The tab badge keeps working while another view is open (cheap: hub states only).
setInterval(async () => {
  if (activeView === 'signalr' || connState === 'down' || connState === 'bg') return;
  try {
    const ctl = new AbortController();
    const timer = setTimeout(() => ctl.abort(), 2000);
    try {
      const state = await (await fetch('/api/signalr', { signal: ctl.signal })).json();
      updateSrBadge(state.hubs || []);
    } finally { clearTimeout(timer); }
  } catch { /* ignore */ }
}, 4000);

// ── Hub cards ────────────────────────────────────────────────────────────────
function srAgo(ms) {
  if (!ms) return '';
  const s = Math.max(0, Math.round((Date.now() - ms) / 1000));
  if (s < 60) return s + ' s';
  if (s < 3600) return Math.floor(s / 60) + ' min';
  return Math.floor(s / 3600) + ' h ' + Math.floor((s % 3600) / 60) + ' min';
}

function srBytes(n) {
  if (n == null) return '';
  if (n < 1024) return n + ' B';
  if (n < 1024 * 1024) return (n / 1024).toFixed(1) + ' KB';
  return (n / 1024 / 1024).toFixed(1) + ' MB';
}

function renderSrHubs() {
  const host = document.getElementById('srhubs');
  if (!srUi.hubs.length) {
    if (srUi.hubsKey !== 'empty') {
      srUi.hubsKey = 'empty';
      host.innerHTML = '';
      const msg = el('div', 'ruleshint');
      msg.append('No hubs yet. Add ', el('code', '', '.WithMauiInspector("chat")'),
        ' to your HubConnectionBuilder (package Immons.Tools.Maui.Inspector.SignalR).');
      host.appendChild(msg);
    }
    return;
  }
  srUi.hubsKey = '';
  const selected = document.getElementById('srhubfilter').value;
  host.innerHTML = '';
  for (const h of srUi.hubs) {
    const card = el('div', 'srhub st-' + h.state.toLowerCase() + (selected === h.name ? ' selected' : ''));
    card.title = 'Click to show only this hub';
    card.onclick = () => {
      const pick = document.getElementById('srhubfilter');
      pick.value = pick.value === h.name ? '' : h.name;
      srFilterChanged();
      renderSrHubs();
    };
    const head = el('div', 'srhubhead');
    head.append(el('span', 'srdot'), el('b', '', h.name), el('span', 'srstate', h.state + (h.attempt ? ' · attempt ' + h.attempt : '')),
      el('span', 'nethint', 'for ' + srAgo(h.stateSince)));
    if (h.transport) head.appendChild(el('span', 'tag', h.transport));
    if (h.protocol) head.appendChild(el('span', 'tag', h.protocol));
    const rc = el('span', 'srreconnects' + (h.reconnects ? ' some' : ''), '↻ ' + h.reconnects);
    rc.title = 'Reconnects: automatic ones and app-driven restarts after a drop';
    head.appendChild(rc);
    card.appendChild(head);

    const meta = [];
    if (h.connectionId) meta.push('id ' + h.connectionId);
    meta.push('↑ ' + h.messagesOut + ' (' + srBytes(h.bytesOut) + ')');
    meta.push('↓ ' + h.messagesIn + ' (' + srBytes(h.bytesIn) + ')');
    if (h.lastPingIn) meta.push('last ping ↓ ' + srAgo(h.lastPingIn) + ' ago');
    meta.push(h.events + '/' + h.capacity + ' events' + (h.dropped ? ' (' + h.dropped + ' dropped)' : ''));
    if (h.instances > 1) meta.push(h.instances + ' connections built');
    card.appendChild(el('div', 'srhubmeta', meta.join('  ·  ')));
    if (h.lastError) card.appendChild(el('div', 'srerr', h.lastError));
    if (h.url) card.appendChild(el('div', 'srurl', h.url));
    host.appendChild(card);
  }
}

// ── Timeline ─────────────────────────────────────────────────────────────────
function srFilterKey() {
  return [document.getElementById('srhubfilter').value, document.getElementById('srmethod').value.trim().toLowerCase(),
    document.getElementById('srpings').checked].join('\u0000');
}

function srMatches(e) {
  const hub = document.getElementById('srhubfilter').value;
  if (hub && e.hub !== hub) return false;
  if (e.kind === 'ping' && !document.getElementById('srpings').checked) return false;
  const q = document.getElementById('srmethod').value.trim().toLowerCase();
  if (!q) return true;
  return [e.method, e.kind, e.state, e.preview, e.error, e.invocationId, e.hub]
    .some(v => v != null && String(v).toLowerCase().includes(q));
}

function srTime(ms) {
  const d = new Date(ms);
  const p = (n, w = 2) => String(n).padStart(w, '0');
  return p(d.getHours()) + ':' + p(d.getMinutes()) + ':' + p(d.getSeconds()) + '.' + p(d.getMilliseconds(), 3);
}

function srRow(e) {
  const row = el('div', 'netrow srrow' + (e.dir ? ' dir-' + e.dir : ' srlife') + (e.error ? ' haserr' : ''));
  row.dataset.seq = e.seq;
  const arrow = e.dir === 'out' ? '↑' : e.dir === 'in' ? '↓' : '•';
  const dirSpan = el('span', 'srarrow', arrow);
  dirSpan.title = e.dir === 'out' ? 'client → server' : e.dir === 'in' ? 'server → client' : 'connection lifecycle';
  const kind = el('span', 'srkind', e.kind + (e.injected ? ' 💉' : ''));
  if (e.injected) kind.title = 'Injected from the panel — the server never sent it';
  const name = e.dir ? (e.method || '') : (e.state || '');
  const method = el('span', 'srmethod' + (!e.dir ? ' st-' + String(e.state || '').split(' ')[0].toLowerCase() : ''), name);
  method.title = name;
  const info = el('span', 'srinfo');
  const bits = [];
  if (e.ms != null) bits.push(el('span', 'srms', e.ms + ' ms'));
  if (e.attempt) bits.push(el('span', 'netdim', '#' + e.attempt));
  if (e.error) bits.push(el('span', 'err', e.error));
  if (e.preview) bits.push(el('span', 'netdim', e.preview));
  else if (!e.dir && e.connectionId) bits.push(el('span', 'netdim', 'id ' + e.connectionId));
  for (const b of bits) { info.append(b, ' '); }
  info.title = [e.error, e.preview].filter(Boolean).join('\n');
  row.append(el('span', 'netdim', srTime(e.time)), dirSpan, el('span', 'srhubname', e.hub), kind, method,
    el('span', 'netnum', e.bytes ? srBytes(e.bytes) : ''), info);
  row.onclick = () => toggleSrDetail(row, e);
  return row;
}

function renderSrEvents(fresh) {
  const host = document.getElementById('srevents');
  const key = srFilterKey();
  if (srUi.renderedKey === key) {
    // Same filters: prepend only what is new (newest on top).
    const rows = fresh.filter(srMatches);
    if (!rows.length) return;
    document.getElementById('srempty')?.remove();
    const head = host.querySelector('.nethead');
    for (const e of rows) head.after(srRow(e));
    const all = host.querySelectorAll('.srrow');
    for (let i = SR_RENDER_LIMIT; i < all.length; i++) {
      const detail = all[i].nextElementSibling;
      if (detail?.classList.contains('netdetail')) detail.remove();
      all[i].remove();
    }
    return;
  }

  srUi.renderedKey = key;
  host.innerHTML = '';
  const head = el('div', 'netrow nethead srrow');
  for (const caption of ['Time', '', 'Hub', 'Kind', 'Method / state', 'Size', 'Details'])
    head.appendChild(el('span', '', caption));
  host.appendChild(head);
  const shown = srUi.events.filter(srMatches).slice(-SR_RENDER_LIMIT).reverse();
  for (const e of shown) host.appendChild(srRow(e));
  if (!shown.length) {
    const empty = el('div', 'netrow netmsg', srUi.events.length ? 'No event matches the filter.' : 'No events yet.');
    empty.id = 'srempty';
    host.appendChild(empty);
  }
  if (srUi.expandedSeq != null) {
    const row = host.querySelector('.srrow[data-seq="' + srUi.expandedSeq + '"]');
    const e = srUi.events.find(x => x.seq === srUi.expandedSeq);
    if (row && e) { srUi.expandedSeq = null; toggleSrDetail(row, e); }
  }
}

function srPretty(text) {
  try { return JSON.stringify(JSON.parse(text), null, 2); } catch { return text; }
}

async function toggleSrDetail(row, e) {
  const existing = row.nextElementSibling?.classList.contains('netdetail') ? row.nextElementSibling : null;
  if (existing) { existing.remove(); srUi.expandedSeq = null; return; }
  document.querySelectorAll('#srevents .netdetail').forEach(d => d.remove());
  srUi.expandedSeq = e.seq;

  let full = e;
  try {
    const r = await fetch('/api/signalr/event?seq=' + e.seq);
    if (r.ok) full = await r.json();
  } catch { /* fall back to the list entry */ }
  if (srUi.expandedSeq !== e.seq) return;

  const detail = el('div', 'netdetail');
  const facts = [
    ['Hub', full.hub], ['Direction', full.dir === 'out' ? '↑ client → server' : full.dir === 'in' ? '↓ server → client' : 'lifecycle'],
    ['Kind', full.kind + (full.injected ? ' (injected from the panel)' : '')], ['Method', full.method], ['State', full.state],
    ['Invocation id', full.invocationId], ['Connection id', full.connectionId], ['Size', full.bytes ? full.bytes + ' bytes' : null],
    ['Duration', full.ms != null ? full.ms + ' ms (invoke → completion)' : null], ['Attempt', full.attempt],
    ['Time', new Date(full.time).toISOString()], ['Error', full.error],
  ];
  const table = el('table', 'srfacts');
  for (const [k, v] of facts) {
    if (v == null || v === '') continue;
    const tr = el('tr');
    tr.append(el('td', 'k', k), el('td', k === 'Error' ? 'err' : '', String(v)));
    table.appendChild(tr);
  }
  detail.appendChild(table);
  if (full.payload != null) {
    detail.appendChild(el('div', 'rulelabel', 'Payload' + (full.truncated ? ' (truncated — MaxPayloadChars)' : '') + ' · secrets masked'));
    const pre = el('pre', 'netpre srpre', srPretty(full.payload));
    detail.appendChild(pre);
    const copy = el('button', 'tabbtn', '⧉ Copy');
    copy.onclick = ev => { ev.stopPropagation(); navigator.clipboard?.writeText(pre.textContent); };
    detail.appendChild(copy);
  }
  if (full.dir === 'in' && full.kind === 'invocation' && full.method) {
    const again = el('button', 'tabbtn', '💉 Inject again…');
    again.title = 'Open the inject form with this call';
    again.onclick = ev => {
      ev.stopPropagation();
      let args = '[]';
      try { args = JSON.stringify(JSON.parse(full.payload).arguments ?? [], null, 2); } catch { /* truncated payload */ }
      openSrInject(full.hub, full.method, args);
    };
    detail.appendChild(again);
  }
  detail.onclick = ev => ev.stopPropagation();
  row.after(detail);
}

// ── Inject (developer tool) ──────────────────────────────────────────────────
function srToggleInject() {
  if (srUi.injectOpen) closeSrInject();
  else openSrInject(document.getElementById('srhubfilter').value || srUi.hubs[0]?.name || '', '', '[]');
}

function closeSrInject() {
  srUi.injectOpen = false;
  document.getElementById('srinject').hidden = true;
  document.getElementById('srInjectBtn').classList.remove('active');
}

function openSrInject(hub, method, args) {
  srUi.injectOpen = true;
  document.getElementById('srInjectBtn').classList.add('active');
  const host = document.getElementById('srinject');
  host.hidden = false;
  host.innerHTML = '';
  const card = el('div', 'srinjectcard');
  const warn = el('div', 'srwarn');
  warn.append(el('b', '', '⚠ Developer tool. '),
    'The message enters this app\'s connection as if the server had sent it: your On("…") handler runs for real — '
    + 'state changes, navigation, local writes. The server never hears about it. Text (JSON / Newtonsoft) protocols only, '
    + 'not with stateful reconnect.');
  card.appendChild(warn);

  const grid = el('div', 'rulegrid');
  const hubPick = el('select');
  hubPick.id = 'srinjecthub';
  const methodBox = Object.assign(el('input'), { type: 'text', id: 'srinjectmethod', value: method, placeholder: 'client method, e.g. ReceiveMessage' });
  grid.append(el('span', 'rulelabel', 'Hub'), hubPick, el('span', 'rulelabel', 'Method'), methodBox);
  card.appendChild(grid);
  card.appendChild(el('div', 'rulelabel', 'Arguments — a JSON array, one item per handler parameter'));
  const argsBox = Object.assign(el('textarea', 'mockbody'), { id: 'srinjectargs', rows: 6, value: args });
  card.appendChild(argsBox);
  const ctl = el('div', 'pendctl');
  const send = el('button', '', '💉 Inject');
  const result = el('span', 'nethint');
  result.id = 'srinjectresult';
  send.onclick = async () => {
    result.textContent = '…';
    result.className = 'nethint';
    try {
      const r = await (await fetch('/api/signalr/inject', {
        method: 'POST',
        body: JSON.stringify({ hub: hubPick.value, method: methodBox.value, args: argsBox.value }),
      })).json();
      result.textContent = r.ok ? '✓ queued — see the timeline (marked 💉)' : '✕ ' + r.error;
      result.className = r.ok ? 'nethint ok' : 'nethint err';
      if (r.ok) setTimeout(loadSignalR, 150);
    } catch (err) {
      result.textContent = '✕ ' + err;
      result.className = 'nethint err';
    }
  };
  ctl.append(send, Object.assign(el('button', '', 'Close'), { onclick: closeSrInject }), result);
  card.appendChild(ctl);
  host.appendChild(card);
  refreshInjectHubs(hub);
}

function refreshInjectHubs(preferred) {
  const pick = document.getElementById('srinjecthub');
  if (!pick) return;
  const current = preferred ?? pick.value;
  const key = srUi.hubs.map(h => h.name + ':' + (h.injectBlocker || '')).join('|');
  if (pick.dataset.key !== key) {
    pick.dataset.key = key;
    pick.length = 0;
    for (const h of srUi.hubs) {
      const o = new Option(h.name + (h.injectBlocker ? ' — ' + h.injectBlocker : ''), h.name);
      pick.add(o);
    }
  }
  if (current && srUi.hubs.some(h => h.name === current)) pick.value = current;
}
