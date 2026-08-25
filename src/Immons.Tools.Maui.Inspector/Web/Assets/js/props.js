// Property panel: sections, editors, layout explorer, filter.

// Set while the cookbook's property popup is open: the sheet renders there, own properties
// first with the inherited sections folded — the same editors, the same apply pipeline.
let propsTarget = null;

function isOwnSection(s) {
  return s.title.endsWith(' properties') && s.title !== 'All properties';
}

function arrangeForCookbook(sections) {
  const own = sections.filter(isOwnSection);
  if (!own.length) return sections;
  return own.concat(sections.filter(s => !isOwnSection(s)).map(s => ({ ...s, folded: true })));
}

async function loadProps(id, keepScroll) {
  if (typeof refreshXamlPreview === 'function') refreshXamlPreview();
  const el = propsTarget || document.getElementById('props');
  // Scroll survives selection changes too — comparing the same section across elements
  // is the common flow; the browser clamps when the new list is shorter.
  const scroll = el.scrollTop;
  const r = await fetch('/api/element/' + id);
  if (!r.ok) {
    const msg = r.status === 404
      ? 'Element is gone — refresh the tree.'
      : 'Inspector error (' + r.status + '): ' + (await r.text());
    el.innerHTML = '';
    el.appendChild(Object.assign(document.createElement('div'), { id: 'empty', textContent: msg }));
    return;
  }
  const data = await r.json();
  el.innerHTML = '';
  currentSource = data.source || null;
  currentIdentity = {
    elementName: data.elementName || '',
    automationId: data.automationId || '',
    type: data.type || '',
    page: data.page || '',
  };
  currentTemplated = !!data.templated;

  if (data.source) {
    const src = document.createElement('div');
    src.id = 'source';
    const m = data.source.match(/^(.*?);assembly=(.*?):(\d+):(\d+)$/);
    src.textContent = m ? (m[1] + ':' + m[3] + ':' + m[4] + '  ·  assembly ' + m[2]) : data.source;
    const all = document.createElement('label');
    all.className = 'allinst';
    const cb = Object.assign(document.createElement('input'), { type: 'checkbox', checked: allInstances });
    cb.onchange = () => { allInstances = cb.checked; };
    all.append(cb, ' ⧉ all instances of this template');
    all.title = 'Apply edits to every element created from this XAML line — all rows of a DataTemplate at once';
    src.appendChild(all);
    el.appendChild(src);
  }

  if (data.layout)
    el.appendChild(renderLayoutExplorer(data.layout));

  const sections = propsTarget ? arrangeForCookbook(data.sections) : data.sections;
  for (const s of sections) {
    const table = document.createElement('table');
    for (const row of s.rows) table.appendChild(renderPropRow(id, s.title, row));

    let sec;
    if (s.group || s.folded) {
      // Grouped sections collapse; the big "All properties" — and every inherited section in
      // the cookbook popup — starts closed.
      sec = document.createElement('details');
      if (s.group !== 'allprops' && !s.folded) sec.open = true;
      const sum = document.createElement('summary');
      sum.appendChild(Object.assign(document.createElement('h2'), { textContent: s.title }));
      sec.appendChild(sum);
    } else {
      sec = document.createElement('div');
      sec.appendChild(Object.assign(document.createElement('h2'), { textContent: s.title }));
    }
    sec.className = 'section';
    sec.appendChild(table);
    el.appendChild(sec);
  }
  el.scrollTop = scroll;
  filterProps(document.getElementById('propfilter').value);
}

// Flutter-style Layout Explorer: children drawn to scale, click to select.
function renderLayoutExplorer(L) {
  const wrap = document.createElement('div');
  wrap.className = 'layoutwrap';

  const head = document.createElement('div');
  head.className = 'layouthead';
  head.textContent = 'Layout explorer — ' + L.kind + '  (' + L.w + '×' + L.h + ' dp)';
  wrap.appendChild(head);

  const scale = Math.min(430 / L.w, 240 / L.h, 3);
  const box = document.createElement('div');
  box.className = 'layoutbox';
  box.style.width = (L.w * scale) + 'px';
  box.style.height = (L.h * scale) + 'px';

  for (const c of L.children) {
    const d = document.createElement('div');
    d.className = 'layoutchild';
    d.style.left = (c.x * scale) + 'px';
    d.style.top = (c.y * scale) + 'px';
    d.style.width = Math.max(2, c.w * scale) + 'px';
    d.style.height = Math.max(2, c.h * scale) + 'px';
    d.title = c.label + '  ·  ' + c.w + '×' + c.h + ' dp @ ' + c.x + ',' + c.y + (c.cell ? '  ·  ' + c.cell : '');
    if (c.w * scale > 46 && c.h * scale > 13)
      d.textContent = c.label.split(' ')[0];
    d.onclick = () => onRowClick(c.id);
    box.appendChild(d);
  }

  wrap.appendChild(box);
  return wrap;
}

function renderPropRow(id, section, row) {
  const tr = document.createElement('tr');
  const k = document.createElement('td');
  k.className = 'k';
  k.textContent = row.isAction ? '' : row.name;
  tr.appendChild(k);
  const v = document.createElement('td');
  // Value and its buttons share one line; badges go underneath.
  const line = document.createElement('div');
  line.className = 'valrow';
  const badges = document.createElement('div');
  v.append(line, badges);

  if (row.isAction) {
    const b = document.createElement('button');
    b.className = 'action';
    b.textContent = row.name;
    b.onclick = async () => {
      await fetch('/api/element/' + id + '/action', { method: 'POST',
        body: JSON.stringify({ section: section, name: row.name }) });
      mirrorAction(section, row.name);
      await refreshAll();
      await loadProps(id, true);
    };
    line.appendChild(b);
  } else if (!row.kind) {
    if (row.swatch) line.appendChild(makeSwatch(row.swatch));
    const span = document.createElement('span');
    span.className = 'ro';
    span.textContent = row.value || '–';
    line.appendChild(span);
  } else if (row.kind === 'Bool') {
    const cb = document.createElement('input');
    cb.type = 'checkbox';
    cb.checked = row.value === 'True' || row.value === 'true';
    cb.onchange = () => apply(id, section, row.name, cb.checked ? 'true' : 'false', cb, true);
    line.appendChild(cb);
  } else if (row.choices) {
    const sel = document.createElement('select');
    for (const c of row.choices) {
      const o = document.createElement('option');
      o.value = o.textContent = c;
      if (c === row.value) o.selected = true;
      sel.appendChild(o);
    }
    sel.onchange = () => apply(id, section, row.name, sel.value, sel, true);
    line.appendChild(sel);
  } else {
    if (row.swatch) line.appendChild(makeSwatch(row.swatch));
    const input = document.createElement('input');
    input.type = 'text';
    input.value = row.value;
    input.onchange = () => apply(id, section, row.name, input.value, input);
    // Type-ahead over the resources that fit this property: "{StaticResource Key}".
    if (row.resources && row.resources.length) {
      const pick = document.createElement('button');
      pick.className = 'rowbtn';
      pick.textContent = '⌄';
      pick.title = row.resources.length + ' matching resource(s) — click to pick';
      pick.onclick = (e) => {
        e.stopPropagation();
        showSuggestionMenu(input, row.resources, e.clientX, e.clientY);
      };
      input.after(pick);
    }
    if (row.resources && row.resources.length) {
      const listId = 'res-' + section.replace(/\W/g, '') + '-' + row.name.replace(/\W/g, '');
      const list = document.createElement('datalist');
      list.id = listId;
      for (const suggestion of row.resources)
        list.appendChild(Object.assign(document.createElement('option'), { value: suggestion }));
      input.setAttribute('list', listId);
      input.title = row.resources.length + ' suggestion(s) for this property';
      line.appendChild(list);
    }
    line.appendChild(input);
  }

  // Data-templated rows share one XAML line — the 🆔 dialog binds AutomationId to item
  // data so every instance gets a different id (also in the tree's right-click menu).
  if (section === 'Element' && row.name === 'AutomationId' && currentTemplated) {
    const uniq = document.createElement('button');
    uniq.className = 'rowbtn';
    uniq.textContent = '🆔';
    uniq.title = 'Unique AutomationId from item data — for DataTemplate / BindableLayout rows';
    uniq.onclick = () => openAutoIdDialog(id);
    line.appendChild(uniq);
  }

  if (row.kind) {
    const dev = document.createElement('button');
    dev.className = 'rowbtn';
    dev.textContent = '⋔︎';
    dev.title = 'Set per platform (OnPlatform) or per idiom (OnIdiom) — written to XAML as a markup extension';
    dev.onclick = () => toggleDeviceEditor(tr, id, section, row);
    line.appendChild(dev);
  }

  if (row.expr) {
    const expr = document.createElement('span');
    expr.className = 'bind';
    expr.textContent = '⋔︎ ' + row.expr;
    expr.title = 'Per-device value applied from the inspector — click ⋔ to edit the entries.';
    expr.style.cursor = 'pointer';
    expr.onclick = () => toggleDeviceEditor(tr, id, section, row);
    badges.appendChild(expr);
  }

  if (row.binding) {
    const bind = document.createElement('span');
    bind.className = 'bind';
    bind.textContent = '⛓︎ ' + row.binding;
    bind.title = 'Value comes from a data binding. Literal edits apply at runtime only (not written to XAML); type {Binding …} to change the binding itself.';
    badges.appendChild(bind);
  }

  if (row.note) {
    const note = document.createElement('span');
    note.className = 'bind';
    note.textContent = '🖌 ' + row.note;
    note.title = 'Where this value comes from. Editing it here changes the shared instance; set {StaticResource …} to point at a different resource.';
    badges.appendChild(note);
  }

  if (row.clearable) {
    const clear = document.createElement('button');
    clear.className = 'rowbtn';
    clear.textContent = '✕';
    clear.title = 'Clear (reset to default/style, removes the XAML attribute)';
    clear.onclick = async () => {
      const r = await fetch('/api/element/' + id + '/property', { method: 'POST',
        body: JSON.stringify({ section: section, name: row.name, clear: true }) });
      if ((await r.json()).ok) {
        mirrorApply(section, row.name, '', true);
        await loadProps(id, true);
      }
    };
    line.appendChild(clear);
  }

  tr.appendChild(v);
  return tr;
}

// Per-device value editor: default (single value), OnPlatform or OnIdiom fields.
// The composed "{OnPlatform iOS=…, Default=…}" goes through the normal apply pipeline:
// the device gets its matching entry live, XAML gets the expression verbatim.
// Parses "{OnPlatform iOS=…, Default=…}" back into { mode, values } for pre-filling the editor.
function parseDeviceExpr(expr) {
  const m = /^\{(?:\w+:)?(OnPlatform|OnIdiom|Adaptive)\s+([\s\S]+)\}$/.exec((expr || '').trim());
  if (!m) return null;
  const out = { mode: m[1], values: {} };
  const s = m[2];
  const unq = (v) => v.length >= 2 && v[0] === "'" && v.endsWith("'") ? v.slice(1, -1) : v;
  const topSplit = (text, sep) => {
    const parts = [];
    let depth = 0, quoted = false, start = 0;
    for (let i = 0; i < text.length; i++) {
      const c = text[i];
      if (c === "'") quoted = !quoted;
      else if (!quoted && c === '{') depth++;
      else if (!quoted && c === '}') depth--;
      else if (!quoted && depth === 0 && c === sep) { parts.push(text.slice(start, i)); start = i + 1; if (sep === '=') return parts.concat([text.slice(start)]); }
    }
    parts.push(text.slice(start));
    return parts;
  };
  for (let part of topSplit(s, ',')) {
    part = part.trim();
    if (!part) continue;
    const kv = topSplit(part, '=');
    if (kv.length < 2) out.values.Default = unq(part);
    else out.values[kv[0].trim()] = unq(kv.slice(1).join('=').trim());
  }
  return out;
}

function toggleDeviceEditor(tr, id, section, row) {
  const next = tr.nextSibling;
  if (next?.classList?.contains('devrow')) { next.remove(); return; }
  document.querySelectorAll('.devrow').forEach(d => d.remove());

  let existing = parseDeviceExpr(row.expr);
  // A nested "{OnIdiom Phone={OnPlatform …}}" (the package-free fallback shape) opens
  // as the Adaptive mode with the entries flattened back into idiom×platform fields.
  if (existing && existing.mode === 'OnIdiom'
      && Object.values(existing.values).some(v => /^\{(?:\w+:)?OnPlatform/.test(v))) {
    const flat = {};
    for (const [k, v] of Object.entries(existing.values)) {
      const inner = parseDeviceExpr(v);
      if (inner && inner.mode === 'OnPlatform' && (k === 'Phone' || k === 'Tablet')) {
        if (inner.values.iOS !== undefined) flat[k + 'IOS'] = inner.values.iOS;
        if (inner.values.Android !== undefined) flat[k + 'Android'] = inner.values.Android;
        if (inner.values.Default !== undefined) flat[k] = inner.values.Default;
      } else {
        flat[k] = v;
      }
    }
    existing = { mode: 'Adaptive', values: flat };
  }

  const sub = document.createElement('tr');
  sub.className = 'devrow';
  const td = document.createElement('td');
  td.colSpan = 2;
  const wrap = document.createElement('div');
  wrap.className = 'devedit';

  const mode = document.createElement('select');
  for (const m of ['default', 'OnPlatform', 'OnIdiom', 'Adaptive']) {
    const o = document.createElement('option');
    o.value = o.textContent = m;
    if (existing && existing.mode === m) o.selected = true;
    mode.appendChild(o);
  }

  const fields = document.createElement('span');
  fields.className = 'devfields';
  const inputs = {};

  const render = () => {
    fields.innerHTML = '';
    for (const k in inputs) delete inputs[k];
    const keys = mode.value === 'default' ? ['Value']
      : mode.value === 'OnPlatform' ? ['Default', 'iOS', 'Android', 'WinUI']
      : mode.value === 'Adaptive'
        ? ['Default', 'Phone', 'PhoneIOS', 'PhoneAndroid', 'Tablet', 'TabletIOS', 'TabletAndroid', 'Desktop']
        : ['Default', 'Phone', 'Tablet', 'Desktop'];
    const preset = existing && existing.mode === mode.value ? existing.values : null;
    for (const key of keys) {
      const label = document.createElement('label');
      label.textContent = key + ' ';
      const input = document.createElement('input');
      input.type = 'text';
      if (preset) {
        const hit = Object.keys(preset).find(k => k.toLowerCase() === key.toLowerCase());
        if (hit !== undefined) input.value = preset[hit];
      } else if (key === 'Value' || key === 'Default') {
        input.value = row.value;
      }
      inputs[key] = input;
      label.appendChild(input);
      fields.appendChild(label);
    }
  };
  mode.onchange = render;
  render();

  const applyBtn = document.createElement('button');
  applyBtn.textContent = '✓ Apply';
  applyBtn.onclick = () => {
    let value;
    if (mode.value === 'default') {
      value = inputs.Value.value;
    } else {
      const quote = (v) => /[,\s]/.test(v) && !v.startsWith('{') ? "'" + v + "'" : v;
      const parts = Object.entries(inputs)
        .filter(([, i]) => i.value.trim() !== '')
        .map(([k, i]) => k + '=' + quote(i.value.trim()));
      if (!parts.length) return;
      if (mode.value === 'Adaptive' && !window.adaptiveAvailable) {
        // The app does not reference the Extensions package — the same fields compose
        // into nested inline OnIdiom/OnPlatform, which compiles with no extra package.
        value = composeNestedAdaptive(inputs, quote);
        if (!value) return;
      } else {
        // Adaptive lives in the Extensions package's xmlns — the "inspector:" placeholder
        // prefix is rewritten by the XAML Updater to whatever prefix the file declares
        // (declaring it on the root when missing).
        const name = mode.value === 'Adaptive' ? 'inspector:Adaptive' : mode.value;
        value = '{' + name + ' ' + parts.join(', ') + '}';
      }
    }
    apply(id, section, row.name, value, applyBtn, true);
  };

  wrap.append(mode, fields, applyBtn);
  td.appendChild(wrap);
  sub.appendChild(td);
  tr.after(sub);
}

function filterProps(q) {
  q = q.trim().toLowerCase();
  for (const sec of document.querySelectorAll('#props .section')) {
    let any = false;
    for (const tr of sec.querySelectorAll('tr')) {
      const name = tr.querySelector('td.k')?.textContent?.toLowerCase() ?? '';
      const show = !q || name.includes(q);
      tr.style.display = show ? '' : 'none';
      if (show) any = true;
    }
    sec.style.display = (!q || any) ? '' : 'none';
  }
}

function makeSwatch(color) {
  const s = document.createElement('span');
  s.className = 'swatch';
  s.style.background = cssColor(color);
  return s;
}

async function apply(id, section, name, value, control, refresh) {
  const r = await fetch('/api/element/' + id + '/property', { method: 'POST',
    body: JSON.stringify({ section: section, name: name, value: value }) });
  const data = await r.json();
  control.classList.toggle('bad', !data.ok);
  if (data.ok)
    mirrorApply(section, name, value);
  // Edited from the cookbook's popup: its capture and the card re-capture right away.
  if (data.ok && propsTarget)
    cookbookOnEdit();
  if (data.ok && data.writeSeq && syncConnected)
    watchWrite(control, data.writeSeq);
  // Picker/checkbox edits (style, enums…) often change other rows — reload them in place.
  if (data.ok && refresh)
    await loadProps(id, true);
  if (data.ok && TREE_LABEL_PROPS.includes(name))
    await refreshAll(true);
  if (data.ok && !document.getElementById('histpanel').hidden)
    await loadHistory();
}

// Small popup listing the resource suggestions for a property editor.
let suggestionMenu = null;

function showSuggestionMenu(input, suggestions, x, y) {
  closeSuggestionMenu();
  suggestionMenu = document.createElement('div');
  suggestionMenu.className = 'ctxmenu';
  for (const suggestion of suggestions.slice(0, 30)) {
    const item = document.createElement('div');
    item.className = 'ctxitem';
    item.textContent = suggestion;
    item.onclick = () => {
      input.value = suggestion;
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      closeSuggestionMenu();
    };
    suggestionMenu.appendChild(item);
  }
  document.body.appendChild(suggestionMenu);
  const rect = suggestionMenu.getBoundingClientRect();
  suggestionMenu.style.left = Math.min(x, innerWidth - rect.width - 8) + 'px';
  suggestionMenu.style.top = Math.min(y, innerHeight - rect.height - 8) + 'px';
  setTimeout(() => document.addEventListener('click', closeSuggestionMenu, { once: true }), 0);
}

function closeSuggestionMenu() {
  if (suggestionMenu) { suggestionMenu.remove(); suggestionMenu = null; }
}

// XAML write feedback: a spinner next to the edited field until the updater's ack lands,
// then ✓ (fades) or ⚠ with the failure reason. No updater ack within ~10 s → give up quietly.
async function watchWrite(control, seq) {
  control.parentElement?.querySelector('.writestate')?.remove();
  const state = document.createElement('span');
  state.className = 'writestate spin';
  state.textContent = '⟳';
  state.title = 'Writing to the XAML file…';
  control.after(state);

  for (let attempt = 0; attempt < 14; attempt++) {
    await new Promise(r => setTimeout(r, 700));
    if (!state.isConnected) return;   // the row was re-rendered — stop quietly
    let d;
    try {
      d = await (await fetch('/api/changes/status?seq=' + seq)).json();
    } catch { continue; }
    if (d.state === 'applied') {
      state.className = 'writestate okmark';
      state.textContent = '✓';
      state.title = 'Written to the XAML file' + (d.message ? ': ' + d.message : '');
      setTimeout(() => state.remove(), 2500);
      return;
    }
    if (d.state === 'failed') {
      state.className = 'writestate failmark';
      state.textContent = '⚠';
      state.title = 'XAML write failed: ' + (d.message || 'see the updater console');
      return;
    }
  }
  state.remove();   // no ack (older updater?) — don't pretend either way
}

// The Adaptive fields expressed without the Extensions package: per-idiom entries whose
// platform variants nest an inline OnPlatform — "{OnIdiom Phone={OnPlatform iOS='16,0',
// Android='8,0'}, Tablet='24,0', Default='0'}".
function composeNestedAdaptive(inputs, quote) {
  const v = (k) => (inputs[k] ? inputs[k].value.trim() : '');
  const parts = [];
  for (const base of ['Phone', 'Tablet']) {
    const ios = v(base + 'IOS');
    const droid = v(base + 'Android');
    if (ios || droid) {
      const inner = [];
      if (ios) inner.push('iOS=' + quote(ios));
      if (droid) inner.push('Android=' + quote(droid));
      if (v(base)) inner.push('Default=' + quote(v(base)));
      parts.push(base + '={OnPlatform ' + inner.join(', ') + '}');
    } else if (v(base)) {
      parts.push(base + '=' + quote(v(base)));
    }
  }
  if (v('Desktop')) parts.push('Desktop=' + quote(v('Desktop')));
  if (v('Default')) parts.push('Default=' + quote(v('Default')));
  return parts.length ? '{OnIdiom ' + parts.join(', ') + '}' : null;
}
