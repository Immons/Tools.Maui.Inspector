// Network tab: breakpoints bar, pending calls, mock rules and request history with bodies.
const OFF_VALUE = '\u0000off';   // sentinel: cannot collide with a scenario name
const netUi = {
  built: false, rulesJson: '', pendingIds: '', historyJson: '',
  expandedSeq: null, ruleFormOpen: false,
};

function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text !== undefined) e.textContent = text;
  return e;
}

async function loadNetwork() {
  const list = document.getElementById('subview-requests');
  if (!netUi.netBuilt) {
    list.innerHTML = '';
    list.append(el('div', '', ''), el('div', '', ''), el('div', '', ''));
    [list.children[0].id, list.children[1].id, list.children[2].id] =
      ['netbar', 'netpending', 'nethistory'];
    netUi.netBuilt = true;
    netUi.pendingIds = ''; netUi.historyJson = '';
    buildBar();
  }

  const [net, icfg] = await Promise.all([
    (await fetch('/api/network')).json(),
    (await fetch('/api/intercept')).json(),
  ]);

  updateBar(icfg);
  renderPending(icfg.pending);
  renderHistory(net.entries);
}

async function loadMocks() {
  const list = document.getElementById('subview-mocks');
  if (!netUi.mocksBuilt) {
    list.innerHTML = '';
    const host = el('div', '', '');
    host.id = 'netrules';
    list.appendChild(host);
    netUi.mocksBuilt = true;
    netUi.rulesJson = '';
  }

  let resp = await (await fetch('/api/mock/rules')).json();
  resp = await syncRulesWithBrowser(resp);
  netUi.scenarioNames = resp.scenarios || [];
  netUi.recState = { on: !!resp.recording, count: resp.recorded || 0 };
  netUi.mockingEnabled = resp.mockingEnabled !== false;
  renderRules(resp.rules, resp.activeScenario || '');
}

// Device memory is per-app-session; the browser keeps the user's copy. An app that
// restarts with an empty rule list gets the browser's set pushed back automatically.
// Deleting rules in the panel updates the browser copy too, so nothing resurrects.
// The backup key includes the app identity: ports are recycled between apps (adb forward,
// auto-assignment), and a different app must never inherit someone else's recorded rules.
async function mockStorageKey() {
  if (netUi.storageKey) return netUi.storageKey;
  try {
    const ping = await (await fetch('/api/ping')).json();
    netUi.storageKey = 'hvMockRules:' + (ping.app || '?') + '|' + (ping.device || '?');
  } catch {
    netUi.storageKey = 'hvMockRules:' + location.origin;
  }
  return netUi.storageKey;
}

async function syncRulesWithBrowser(resp) {
  try {
    const key = await mockStorageKey();
    let saved = JSON.parse(localStorage.getItem(key) || 'null');
    if (Array.isArray(saved)) saved = { rules: saved, scenarios: [], activeScenario: '' }; // legacy
    // Restoring a large set takes many POSTs; without this guard the polling loop keeps seeing
    // an empty list while the push is still in flight and restores the same rules again.
    if (!resp.rules.length && saved && (saved.rules || []).length && !netUi.restoring) {
      netUi.restoring = true;
      try {
        await pushMockState(location.origin, saved);
        resp = await (await fetch('/api/mock/rules')).json();
      } finally {
        netUi.restoring = false;
      }
    }
    if (netUi.restoring) return resp;   // a restore is running — don't overwrite the backup with an empty list
    localStorage.setItem(key, JSON.stringify({
      scenarios: resp.scenarios || [], activeScenario: resp.activeScenario || '', rules: resp.rules,
    }));
  } catch { /* storage unavailable — device-only rules still work */ }
  return resp;
}

// Applies a full mock state (scenarios + rules + active scenario) to one device in ONE request.
// Sending it rule by rule made a restored set trickle in visibly — and cost a full rewrite of the
// stored set per rule on the Preferences backend.
async function pushMockState(base, state) {
  await fetch(base + '/api/mock/rules/import', {
    method: 'POST',
    body: JSON.stringify({
      scenarios: state.scenarios || [],
      activeScenario: state.activeScenario || '',
      rules: (state.rules || []).map(r => ({ ...r, id: 0 })),
    }),
  });
}

// ── Breakpoints bar ──────────────────────────────────────────────────────────
function buildBar() {
  const bar = document.getElementById('netbar');
  bar.className = 'netbar';
  bar.append(
    makeBpToggle('bpReq', '⏸ Requests'),
    makeBpToggle('bpResp', '⏸ Responses'),
    Object.assign(el('input'), { id: 'bpFilter', type: 'text', placeholder: 'breakpoint URL filter…', onchange: postBpConfig }),
    Object.assign(el('input'), {
      id: 'reqfilter', type: 'text', placeholder: 'Filter requests…',
      className: 'rulefilter',
      value: netUi.reqFilter || '',
      oninput: e => { netUi.reqFilter = e.target.value; applyRequestFilter(netUi.reqFilter); },
    }),
    Object.assign(el('button', 'tabbtn', '🧹 Clear'), {
      title: 'Drop all recorded requests',
      onclick: async () => {
        await fetch('/api/network/clear', { method: 'POST', body: '{}' });
        netUi.historyJson = '';
        loadNetwork();
      },
    }),
    Object.assign(el('span', 'nethint'),
      { textContent: 'Breakpoints pause matching calls until you continue them (mind HttpClient.Timeout).' }));
}

function applyRequestFilter(query) {
  const q = (query || '').trim().toLowerCase();
  let shown = 0;
  for (const row of document.querySelectorAll('#nethistory .netrow')) {
    if (row.id === 'reqempty' || row.classList.contains('nethead')) continue;
    const hit = !q || (row.dataset.search || '').includes(q);
    row.hidden = !hit;
    if (hit) shown++;
    // A detail panel follows its row.
    const detail = row.nextElementSibling;
    if (detail?.classList.contains('netdetail'))
      detail.hidden = !hit;
  }
  const empty = document.getElementById('reqempty');
  if (empty)
    empty.hidden = shown > 0 || !q;
}

function makeBpToggle(id, label) {
  const wrap = el('label', 'bptoggle');
  const cb = Object.assign(el('input'), { id: id, type: 'checkbox', onchange: postBpConfig });
  wrap.append(cb, ' ' + label);
  return wrap;
}

function postBpConfig() {
  fetch('/api/intercept/config', { method: 'POST', body: JSON.stringify({
    req: document.getElementById('bpReq').checked,
    resp: document.getElementById('bpResp').checked,
    filter: document.getElementById('bpFilter').value,
  }) });
}

function updateBar(cfg) {
  const req = document.getElementById('bpReq');
  const resp = document.getElementById('bpResp');
  const filter = document.getElementById('bpFilter');
  if (req.checked !== cfg.req) req.checked = cfg.req;
  if (resp.checked !== cfg.resp) resp.checked = cfg.resp;
  if (document.activeElement !== filter && filter.value !== cfg.filter) filter.value = cfg.filter;
}

// ── Pending breakpoint calls ─────────────────────────────────────────────────
function renderPending(pending) {
  // "(none)" keeps the empty list distinct from the "force rebuild" sentinel (null/'').
  const ids = pending.map(p => p.id + p.phase).join(',') || '(none)';
  if (ids === netUi.pendingIds) return; // keep user's edits in the textareas
  netUi.pendingIds = ids;

  const host = document.getElementById('netpending');
  host.innerHTML = '';
  for (const p of pending) {
    const card = el('div', 'pendcard');
    const head = el('div', 'pendhead');
    head.append(
      el('span', 'pendphase', p.phase === 'request' ? 'REQUEST ⏸' : 'RESPONSE ⏸'),
      el('span', '', ' ' + p.method + ' ' + p.url + '  ·  ' + p.time));
    card.appendChild(head);

    const body = Object.assign(el('textarea', 'mockbody'), { value: p.body, rows: 6 });
    card.appendChild(body);

    const controls = el('div', 'pendctl');
    let status = null;
    if (p.phase === 'response') {
      status = Object.assign(el('input'), { type: 'text', value: p.status, title: 'Status code' });
      status.style.width = '70px';
      controls.append('Status: ', status);
    }
    const cont = el('button', '', '▶ Continue');
    cont.onclick = async () => {
      await fetch('/api/intercept/resume', { method: 'POST', body: JSON.stringify({
        id: p.id, body: body.value, status: status ? parseInt(status.value) || null : null,
      }) });
      netUi.pendingIds = '';
      loadNetwork();
    };
    const abort = el('button', '', '✕ Abort');
    abort.onclick = async () => {
      await fetch('/api/intercept/abort', { method: 'POST', body: JSON.stringify({ id: p.id }) });
      netUi.pendingIds = '';
      loadNetwork();
    };
    controls.append(cont, abort);
    card.appendChild(controls);
    host.appendChild(card);
  }
}

// ── Mock rules ───────────────────────────────────────────────────────────────
function renderRules(rules, activeScenario) {
  const json = JSON.stringify(rules) + '|' + activeScenario + '|' + JSON.stringify(netUi.scenarioNames) + '|' + JSON.stringify(netUi.recState) + '|' + !!netUi.showAllRules + '|' + !!netUi.mockingEnabled;
  // netUi.ruleFilter is re-applied after each render instead of forcing a rebuild.
  if (json === netUi.rulesJson) return;
  netUi.rulesJson = json;

  const host = document.getElementById('netrules');
  host.innerHTML = '';
  const head = el('div', 'ruleshead');
  head.append(el('b', '', 'Mock rules (' + rules.length + ')'),
    Object.assign(el('button', 'tabbtn', '+ Add rule'), { onclick: () => openRuleForm(null) }),
    Object.assign(el('button', 'tabbtn', '⬆ Export'), {
      title: 'Download scenarios + rules as one JSON — reusable on any device/session',
      onclick: exportRules,
    }),
    Object.assign(el('button', 'tabbtn', '⬇ Import'), {
      title: 'Load a rule set from a JSON file and push it to this device',
      onclick: importRules,
    }),
    recordControls(),
    ruleFilterBox(),
    scenarioPicker(rules, activeScenario));
  host.appendChild(head);

  if (!netUi.mockingEnabled) {
    const off = el('div', 'ruleshint mockoff');
    off.textContent = 'Mocking is off — no rule matches, the app talks to the real API. '
      + 'Rules are kept; pick a scenario (or "(none)") above to switch back on.';
    host.appendChild(off);
  }

  const inEffect = r => !(r.scenarios || []).length || (r.scenarios || []).includes(activeScenario);
  const visibleRules = (activeScenario && !netUi.showAllRules) ? rules.filter(inEffect) : rules;
  const hiddenCount = rules.length - visibleRules.length;

  for (const r of visibleRules) {
    const row = el('div', 'rulerow');
    row.dataset.search = [r.name, r.method, r.urlPattern, (r.scenarios || []).join(' '),
      r.responseBody, r.requestBody, r.status].filter(Boolean).join(' ').toLowerCase();
    const ruleScenarios = r.scenarios || [];
    if (ruleScenarios.length && !ruleScenarios.includes(activeScenario))
      row.classList.add('inactive');
    const cb = Object.assign(el('input'), { type: 'checkbox', checked: r.enabled, title: 'Enabled' });
    cb.onchange = async () => {
      await fetch('/api/mock/rules/enable', { method: 'POST', body: JSON.stringify({ id: r.id, on: cb.checked }) });
      mirrorRulesSync();
      netUi.rulesJson = ''; loadMocks();
    };
    const summary = [];
    if (r.shortCircuit) summary.push('mock (no network)');
    if (r.status) summary.push('status ' + r.status);
    if (r.requestBody) summary.push('req body');
    if (r.responseBody) summary.push('resp body');
    if (r.delayMs) summary.push('+' + r.delayMs + ' ms');
    if (r.failMode) summary.push(r.failMode);
    const label = el('span', 'rulesum',
      (ruleScenarios.length ? '[' + ruleScenarios.join(', ') + '] ' : '') + (r.name ? r.name + '  ·  ' : '')
      + r.method + ' ' + r.urlPattern + '  →  ' + (summary.join(', ') || 'log only'));
    const edit = Object.assign(el('button', 'clearbtn', '✎'), { onclick: () => openRuleForm(r), title: 'Edit' });
    const copy = Object.assign(el('button', 'clearbtn', '⧉'), {
      title: 'Duplicate — opens a copy ready to tweak',
      onclick: () => openRuleForm({ ...r, id: 0, name: (r.name || 'rule') + ' copy' }),
    });
    const del = Object.assign(el('button', 'clearbtn', '🗑'), { onclick: async () => {
      await fetch('/api/mock/rules/delete', { method: 'POST', body: JSON.stringify({ id: r.id }) });
      mirrorRulesSync();
      netUi.rulesJson = ''; loadMocks();
    } });
    row.append(cb, label, edit, copy, del);
    host.appendChild(row);
  }

  if (hiddenCount > 0) {
    const note = el('div', 'ruleshint');
    note.append(hiddenCount + ' rule(s) belong to other scenarios and are hidden. ');
    note.appendChild(Object.assign(el('button', 'tabbtn', 'Show all'), {
      onclick: () => { netUi.showAllRules = true; netUi.rulesJson = ''; loadMocks(); },
    }));
    host.appendChild(note);
  } else if (netUi.showAllRules && activeScenario) {
    const note = el('div', 'ruleshint');
    note.appendChild(Object.assign(el('button', 'tabbtn', 'Show only "' + activeScenario + '"'), {
      onclick: () => { netUi.showAllRules = false; netUi.rulesJson = ''; loadMocks(); },
    }));
    host.appendChild(note);
  }

  const empty = el('div', 'netrow', 'No rule matches the filter.');
  empty.id = 'rulesempty';
  empty.hidden = true;
  host.appendChild(empty);

  applyRuleFilter(netUi.ruleFilter || '');

}

// The form is a modal: with a long rule list an inline editor at the bottom is unreachable.
function openRuleForm(rule) {
  closeRuleForm(true);
  netUi.ruleFormOpen = true;

  const backdrop = el('div', 'modalback');
  backdrop.id = 'ruleformback';
  backdrop.onclick = e => { if (e.target === backdrop) closeRuleForm(); };

  const form = el('div', 'ruleform');
  form.id = 'ruleform';
  backdrop.appendChild(form);
  document.body.appendChild(backdrop);
  document.addEventListener('keydown', onRuleFormKey);

  const r = rule || {};
  const head = el('div', 'ruleshead');
  head.append(el('b', '', r.id ? 'Edit rule' : 'New rule'),
    Object.assign(el('button', 'clearbtn', '✕'), { onclick: () => closeRuleForm(), title: 'Close (Esc)' }));
  form.appendChild(head);

  const grid = el('div', 'rulegrid');
  const name = field(grid, 'Name', input('text', r.name || ''));
  const method = field(grid, 'Method', select(['*', 'GET', 'POST', 'PUT', 'PATCH', 'DELETE'], r.method || '*'));
  const pattern = field(grid, 'URL pattern', input('text', r.urlPattern || '', '*/api/plans/*  (substring or * wildcard; most specific rule wins)'));
  const status = field(grid, 'Force status', input('text', r.status || '', 'e.g. 500 (empty = keep)'));
  const delay = field(grid, 'Delay ms', input('text', r.delayMs || ''));
  const fail = field(grid, 'Fail', select(['', 'timeout', 'error'], r.failMode || ''));
  const short = field(grid, 'Mock (no network)', Object.assign(el('input'), { type: 'checkbox', checked: !!r.shortCircuit }));
  form.appendChild(grid);

  // Rule ↔ scenarios: any subset of the registry (empty = global rule).
  const scenarioBoxes = {};
  const names = netUi.scenarioNames || [];
  if (names.length) {
    form.appendChild(el('div', 'rulelabel', 'Scenarios (none checked = global rule)'));
    const rowEl = el('div', 'scenariochecks');
    for (const name of names) {
      const label = el('label', '');
      const cb = Object.assign(el('input'), { type: 'checkbox', checked: (r.scenarios || []).includes(name) });
      scenarioBoxes[name] = cb;
      label.append(cb, ' ' + name);
      rowEl.appendChild(label);
    }
    form.appendChild(rowEl);
  }

  const reqBody = bodyEditor(form, 'Request body override (empty = keep)', r.requestBody || '');
  const respBody = bodyEditor(form, 'Response body override (empty = keep)', r.responseBody || '');

  addHistoryPicker(form, reqBody, respBody);

  const controls = el('div', 'pendctl');
  const save = el('button', '', '💾 Save rule');
  save.onclick = async () => {
    await fetch('/api/mock/rules/save', { method: 'POST', body: JSON.stringify({
      id: r.id || 0,
      enabled: r.enabled !== false,
      name: name.value.trim(),
      method: method.value,
      urlPattern: pattern.value.trim(),
      status: parseInt(status.value) || null,
      delayMs: parseInt(delay.value) || 0,
      failMode: fail.value,
      shortCircuit: short.checked,
      scenarios: Object.entries(scenarioBoxes).filter(([, cb]) => cb.checked).map(([n]) => n),
      requestBody: reqBody.value,
      responseBody: respBody.value,
    }) });
    mirrorRulesSync();
    closeRuleForm();
  };
  const cancel = el('button', '', 'Cancel');
  cancel.onclick = closeRuleForm;
  controls.append(save, cancel);
  form.appendChild(controls);
}

function closeRuleForm(silent) {
  document.getElementById('ruleformback')?.remove();
  document.removeEventListener('keydown', onRuleFormKey);
  netUi.ruleFormOpen = false;
  if (silent) return;
  netUi.rulesJson = '';
  loadMocks();
}

function onRuleFormKey(e) {
  if (e.key === 'Escape') closeRuleForm();
}

function field(grid, label, control) {
  grid.append(el('span', 'rulelabel', label), control);
  return control;
}

function input(type, value, placeholder) {
  return Object.assign(el('input'), { type: type, value: value, placeholder: placeholder || '' });
}

function select(options, value) {
  const s = el('select');
  for (const o of options) {
    const opt = Object.assign(el('option'), { value: o, textContent: o === '' ? '(none)' : o });
    if (o === value) opt.selected = true;
    s.appendChild(opt);
  }
  return s;
}

function bodyEditor(form, label, value) {
  form.appendChild(el('div', 'rulelabel', label));
  const area = Object.assign(el('textarea', 'mockbody'), { value: value, rows: 4 });
  form.appendChild(area);
  return area;
}

// "Z historii": pick a captured call and pull its bodies into the editors.
function addHistoryPicker(form, reqBody, respBody) {
  const row = el('div', 'histpick');
  const pick = el('select');
  pick.appendChild(Object.assign(el('option'), { value: '', textContent: '— insert body from history —' }));
  fetch('/api/network').then(r => r.json()).then(d => {
    for (const e of d.entries.filter(e => e.hasBody)) {
      pick.appendChild(Object.assign(el('option'), {
        value: e.seq,
        textContent: e.time + ' ' + e.method + ' ' + e.status + ' ' + e.url.slice(0, 80),
      }));
    }
  });
  const toReq = el('button', '', '⬇ request');
  const toResp = el('button', '', '⬇ response');
  const pull = async (target, key) => {
    if (!pick.value) return;
    const d = await (await fetch('/api/network/body?seq=' + pick.value)).json();
    if (d[key] != null) target.value = d[key];
  };
  toReq.onclick = () => pull(reqBody, 'request');
  toResp.onclick = () => pull(respBody, 'response');
  row.append(pick, toReq, toResp);
  form.appendChild(row);
}

// Copies a scenario and every rule tagged with it, so a variant can be tweaked in isolation.
async function duplicateScenario(source) {
  if (!source) { alert('Pick the scenario to duplicate first.'); return; }
  const name = prompt('Name of the copy:', source + '-copy');
  if (!name || !name.trim()) return;
  const target = name.trim();

  const state = await (await fetch('/api/mock/rules')).json();
  await fetch('/api/mock/rules/scenario/add', { method: 'POST', body: JSON.stringify({ name: target }) });

  for (const r of state.rules.filter(r => (r.scenarios || []).includes(source))) {
    const scenarios = [...new Set([...(r.scenarios || []).filter(s => s !== source), target])];
    await fetch('/api/mock/rules/save', { method: 'POST', body: JSON.stringify({ ...r, id: 0, scenarios: scenarios }) });
  }

  await fetch('/api/mock/rules/scenario', { method: 'POST', body: JSON.stringify({ name: target }) });
  mirrorRulesSync();
  netUi.rulesJson = '';
  loadMocks();
}

// Search across name, method, URL pattern, scenarios, status and bodies.
function ruleFilterBox() {
  const input = Object.assign(el('input'), {
    type: 'text',
    id: 'rulefilter',
    placeholder: 'Filter rules…',
    value: netUi.ruleFilter || '',
    oninput: e => {
      netUi.ruleFilter = e.target.value;
      applyRuleFilter(netUi.ruleFilter);
    },
  });
  input.className = 'rulefilter';
  return input;
}

function applyRuleFilter(query) {
  const q = (query || '').trim().toLowerCase();
  let shown = 0;
  for (const row of document.querySelectorAll('#netrules .rulerow')) {
    const hit = !q || (row.dataset.search || '').includes(q);
    row.hidden = !hit;
    if (hit) shown++;
  }
  const empty = document.getElementById('rulesempty');
  if (empty)
    empty.hidden = shown > 0 || !q;
}

// Record the live request path into a scenario: Start → use the app → Stop names the
// scenario and every unique call becomes a no-network mock rule tagged with it.
function recordControls() {
  const wrap = el('span', 'recwrap');
  const rec = netUi.recState || { on: false, count: 0 };
  if (!rec.on) {
    const start = Object.assign(el('button', 'tabbtn', '⏺ Record'), {
      title: 'Start recording the request path into a new scenario',
      onclick: async () => {
        await fetch('/api/mock/record/start', { method: 'POST', body: '{}' });
        netUi.rulesJson = '';
        loadMocks();
      },
    });
    wrap.appendChild(start);
    return wrap;
  }

  wrap.appendChild(el('span', 'recbadge', '● REC ' + rec.count));
  const stop = Object.assign(el('button', 'tabbtn', '⏹ Stop'), {
    title: 'Stop and save the captured calls as a scenario',
    onclick: async () => {
      const name = prompt('Scenario name for the recording:', 'recorded-' + new Date().toISOString().slice(11, 16).replace(':', ''));
      if (name === null) return;
      const r = await (await fetch('/api/mock/record/stop', { method: 'POST', body: JSON.stringify({ name: name.trim() }) })).json();
      alert('Saved ' + r.rules + ' rule(s) into scenario "' + (name.trim() || 'recorded') + '".');
      mirrorRulesSync();
      netUi.rulesJson = '';
      loadMocks();
    },
  });
  const cancel = Object.assign(el('button', 'clearbtn', '✕'), {
    title: 'Discard the recording',
    onclick: async () => {
      await fetch('/api/mock/record/cancel', { method: 'POST', body: '{}' });
      netUi.rulesJson = '';
      loadMocks();
    },
  });
  wrap.append(stop, cancel);
  return wrap;
}

// Scenario switch: "user with such-and-such settings" — activating a scenario turns its
// rules on (they outrank global rules on ties); "" means global rules only.
function scenarioPicker(rules, activeScenario) {
  const wrap = el('span', 'scenariowrap');
  wrap.append('Scenario: ');
  const pick = el('select');
  const names = [...new Set([...(netUi.scenarioNames || []), ...rules.flatMap(r => r.scenarios || [])])]
    .sort((a, b) => a.localeCompare(b));
  netUi.scenarioNames = names; // form checkboxes reuse the healed list
  // OFF is not a scenario — it is the master switch, parked in the same picker because that is
  // where you look when you want the app to stop being mocked.
  const off = Object.assign(el('option'), { value: OFF_VALUE, textContent: 'off — mocking disabled' });
  if (!netUi.mockingEnabled) off.selected = true;
  pick.appendChild(off);
  for (const name of ['', ...names]) {
    const o = Object.assign(el('option'), { value: name, textContent: name === '' ? '(none — global rules)' : name });
    if (netUi.mockingEnabled && name === activeScenario) o.selected = true;
    pick.appendChild(o);
  }
  if (activeScenario && !names.includes(activeScenario)) {
    const o = Object.assign(el('option'), { value: activeScenario, textContent: activeScenario });
    o.selected = netUi.mockingEnabled;
    pick.appendChild(o);
  }
  pick.onchange = async () => {
    const enabled = pick.value !== OFF_VALUE;
    await fetch('/api/mock/rules/mocking', { method: 'POST', body: JSON.stringify({ enabled: enabled }) });
    mirrorFanOut('/api/mock/rules/mocking', { enabled: enabled });
    if (enabled) {
      await fetch('/api/mock/rules/scenario', { method: 'POST', body: JSON.stringify({ name: pick.value }) });
      mirrorFanOut('/api/mock/rules/scenario', { name: pick.value });
    }
    netUi.rulesJson = '';
    netUi.showAllRules = false;
    loadMocks();
  };
  const add = Object.assign(el('button', 'tabbtn', '+'), {
    title: 'Create a scenario',
    onclick: async () => {
      const name = prompt('Scenario name (e.g. premium-user):');
      if (!name || !name.trim()) return;
      await fetch('/api/mock/rules/scenario/add', { method: 'POST', body: JSON.stringify({ name: name.trim() }) });
      mirrorRulesSync();
      netUi.rulesJson = '';
      loadMocks();
    },
  });
  const dup = Object.assign(el('button', 'tabbtn', '⧉'), {
    title: 'Duplicate the selected scenario with all its rules',
    onclick: () => duplicateScenario(pick.value),
  });
  const del = Object.assign(el('button', 'tabbtn', '🗑'), {
    title: 'Remove the selected scenario (rules lose the tag, they are not deleted)',
    onclick: async () => {
      if (!pick.value || !confirm('Remove scenario "' + pick.value + '"? Rules stay, only the tag is removed.')) return;
      await fetch('/api/mock/rules/scenario/remove', { method: 'POST', body: JSON.stringify({ name: pick.value }) });
      mirrorRulesSync();
      netUi.rulesJson = '';
      loadMocks();
    },
  });
  wrap.append(pick, add, dup, del);
  return wrap;
}

// The whole Mocks state travels with the user as one JSON file — scenarios, rules and
// the active scenario — usable across devices and sessions.
async function exportRules() {
  const state = await (await fetch('/api/mock/rules')).json();
  const blob = new Blob([JSON.stringify({
    scenarios: state.scenarios || [], activeScenario: state.activeScenario || '', rules: state.rules,
  }, null, 2)], { type: 'application/json' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = 'hv-mock-scenarios.json';
  a.click();
  URL.revokeObjectURL(a.href);
}

function importRules() {
  const picker = document.createElement('input');
  picker.type = 'file';
  picker.accept = '.json,application/json';
  picker.onchange = async () => {
    const file = picker.files[0];
    if (!file) return;
    try {
      let state = JSON.parse(await file.text());
      if (Array.isArray(state)) {
        // Legacy rules-only file: rebuild the registry from rule tags.
        state = { rules: state, scenarios: [...new Set(state.flatMap(r => r.scenarios || []))], activeScenario: '' };
      }
      const replace = confirm('Replace the current rules and scenarios?\nOK = replace, Cancel = merge with existing.');
      if (replace) {
        const current = await (await fetch('/api/mock/rules')).json();
        for (const r of current.rules)
          await fetch('/api/mock/rules/delete', { method: 'POST', body: JSON.stringify({ id: r.id }) });
        for (const name of current.scenarios || [])
          await fetch('/api/mock/rules/scenario/remove', { method: 'POST', body: JSON.stringify({ name: name }) });
      }
      await pushMockState(location.origin, state);
      mirrorRulesSync();
      netUi.rulesJson = '';
      loadMocks();
    } catch (e) {
      alert('Invalid scenarios file: ' + e.message);
    }
  };
  picker.click();
}

// ── History ──────────────────────────────────────────────────────────────────
// Bytes for the Size column: "1.2 MB"; a pending upload reads "12.0 / 300.0 MB ↑".
function netSize(n) {
  if (n == null) return '';
  if (n < 1024) return n + ' B';
  if (n < 1024 * 1024) return (n / 1024).toFixed(1) + ' KB';
  return (n / 1024 / 1024).toFixed(1) + ' MB';
}

function netSizeCell(e) {
  if (e.pending) {
    if (e.sent != null && e.requestLength) return netSize(e.sent) + ' / ' + netSize(e.requestLength) + ' ↑';
    if (e.sent != null) return netSize(e.sent) + ' ↑';
    return e.requestLength ? '0 / ' + netSize(e.requestLength) + ' ↑' : '';
  }
  if (e.received != null && e.bytes != null && e.received < e.bytes) return netSize(e.received) + ' / ' + netSize(e.bytes) + ' ↓';
  if (e.bytes != null) return e.bytes + ' B';
  if (e.received != null) return netSize(e.received) + ' ↓';
  return '';
}

// Rows of calls in flight change every poll (duration, bytes) — those cells are updated in place,
// so an opened detail row survives a 300 MB upload that takes minutes.
function netStructure(entries) {
  return JSON.stringify(entries.map(e => [e.seq, e.status, e.error, e.tag, e.url, !!e.pending, e.hasBody]));
}

function renderHistory(entries) {
  const structure = netStructure(entries);
  if (structure === netUi.historyJson) {
    for (const e of entries) {
      const row = document.querySelector('#nethistory .netrow[data-seq="' + e.seq + '"]');
      if (!row) continue;
      row.querySelector('.netms').textContent = Math.round(e.ms) + ' ms';
      row.querySelector('.netsize').textContent = netSizeCell(e);
    }
    return;
  }
  netUi.historyJson = structure;

  const host = document.getElementById('nethistory');
  host.innerHTML = entries.length ? '' : '<div class="netrow netmsg">No requests yet — add MauiInspectorHttpHandler to your HttpClient.</div>';
  if (entries.length) {
    const head = el('div', 'netrow nethead');
    for (const caption of ['Time', 'St.', 'Method', 'URL', 'Duration', 'Size', 'Rule'])
      head.appendChild(el('span', '', caption));
    host.appendChild(head);
  }
  for (const e of entries) {
    const div = el('div', 'netrow' + (e.pending ? ' netpend' : ''));
    div.dataset.seq = e.seq;
    div.dataset.search = [e.method, e.url, e.status, e.error, e.tag, e.time, e.pending ? 'pending' : null]
      .filter(v => v !== null && v !== undefined).join(' ').toLowerCase();
    const status = e.pending
      ? el('span', 'pend', '⏳')
      : el('span', (e.status >= 200 && e.status < 400) ? 'ok' : 'err', e.error ? e.error : e.status);
    if (e.pending) status.title = 'In flight — the row updates until the response arrives';
    const url = el('span', 'neturl', e.url);
    url.title = e.url;
    div.append(
      el('span', 'netdim', e.time),
      status,
      el('span', '', e.method),
      url,
      el('span', 'netnum netms', Math.round(e.ms) + ' ms'),
      el('span', 'netnum netsize', netSizeCell(e)),
      el('span', 'nettag', e.tag || ''));
    // Every row opens — a call whose body was not captured (binary, empty, over the size cap)
    // must still be turnable into a mock rule.
    div.style.cursor = 'pointer';
    div.title = e.hasBody ? 'Click for request/response bodies' : 'Click to mock this call';
    div.onclick = () => toggleDetail(div, e);
    host.appendChild(div);
  }

  const empty = el('div', 'netrow netmsg', 'No request matches the filter.');
  empty.id = 'reqempty';
  empty.hidden = true;
  host.appendChild(empty);

  applyRequestFilter(netUi.reqFilter || '');
}

async function toggleDetail(row, e) {
  const existing = row.nextSibling?.classList?.contains('netdetail') ? row.nextSibling : null;
  if (existing) { existing.remove(); netUi.expandedSeq = null; return; }
  document.querySelectorAll('.netdetail').forEach(d => d.remove());
  netUi.expandedSeq = e.seq;

  const d = await (await fetch('/api/network/body?seq=' + e.seq)).json();
  const detail = el('div', 'netdetail');
  if (d.request != null) {
    detail.appendChild(el('div', 'rulelabel', 'Request body'));
    detail.appendChild(el('pre', 'netpre', d.request));
  }
  if (d.response != null) {
    detail.appendChild(el('div', 'rulelabel', 'Response body'));
    detail.appendChild(el('pre', 'netpre', d.response));
  }
  if (d.request == null && d.response == null) {
    detail.appendChild(el('div', 'ruleshint',
      'No body captured — it was empty, binary, or larger than options.MaxCapturedBodyBytes'
      + (e.bytes != null ? ' (this one: ' + Math.round(e.bytes / 1024) + ' KB)' : '')
      + '. You can still mock the call and type the response yourself.'));
  }
  const mk = el('button', '', '⚡ Mock this (→ rule)');
  mk.onclick = () => {
    showView('network');
    showNetworkSub('mocks');
    openRuleForm({
      method: e.method, urlPattern: e.url.replace(/\?…$/, ''), status: e.status,
      responseBody: d.response || '', shortCircuit: true, enabled: true,
    });
  };
  detail.appendChild(mk);
  row.after(detail);
}
