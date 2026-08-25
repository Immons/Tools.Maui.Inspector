// Resources popup: dictionaries with editable colors, scalars and style setters —
// all written back to the owning XAML file by the updater.

let resourcesLoaded = false;

function toggleResources() {
  const back = document.getElementById('resback');
  back.hidden = !back.hidden;
  document.getElementById('resBtn').classList.toggle('active', !back.hidden);
  if (!back.hidden) {
    loadResources();
    document.getElementById('ressearch').focus();
  }
}

async function loadResources() {
  const list = document.getElementById('resourcelist');
  let data;
  try {
    data = await (await fetch('/api/resources')).json();
  } catch {
    list.innerHTML = '<div class="hentry">App not reachable.</div>';
    return;
  }

  list.innerHTML = '';
  for (const group of data.groups) {
    const section = el('div', 'resgroup');
    section.appendChild(el('h2', '', group.name));
    for (const entry of group.entries) {
      section.appendChild(renderResourceRow(entry));
      if (entry.kind === 'style' && entry.setters) {
        for (const setter of entry.setters) {
          section.appendChild(renderSetterRow(entry, setter));
          if (setter.resourceKey) section.appendChild(renderSetterResourceRow(setter));
        }
      }
    }
    list.appendChild(section);
  }
  resourcesLoaded = true;
  filterResources(document.getElementById('ressearch').value);
}

function renderResourceRow(entry) {
  const row = el('div', 'resrow');
  row.dataset.search = (entry.key + ' ' + (entry.targetType || '') + ' ' + entry.value).toLowerCase();
  row.appendChild(el('span', 'reskey', entry.key));

  const isColor = entry.kind === 'color' || entry.kind === 'brush';
  const editable = isColor || ['number', 'text', 'bool', 'thickness', 'cornerradius', 'shadow'].includes(entry.kind);

  if (!editable) {
    row.appendChild(el('span', 'resvalue', entry.value));
    return row;
  }

  let swatch = null;
  if (isColor) {
    swatch = el('span', 'swatch');
    swatch.style.background = entry.value;
    row.appendChild(swatch);
  }
  const input = document.createElement('input');
  input.type = 'text';
  input.value = entry.value;
  input.title = 'Press Enter to apply — DynamicResource consumers update live and the value is written back to the dictionary file';
  input.onkeydown = async (e) => {
    if (e.key !== 'Enter') return;
    const r = await (await fetch('/api/resources/set', {
      method: 'POST', body: JSON.stringify({ key: entry.key, value: input.value }),
    })).json();
    input.classList.toggle('bad', !r.ok);
    markRecorded(input, r);
    if (r.ok && swatch) swatch.style.background = input.value;
    if (r.ok) cookbookOnEdit();
  };
  row.appendChild(input);
  return row;
}

function renderSetterRow(styleEntry, setter) {
  const row = el('div', 'resrow setter');
  row.dataset.search = (styleEntry.key + ' ' + setter.property + ' ' + setter.value).toLowerCase();
  row.appendChild(el('span', 'reskey', setter.property));

  if (!setter.editable) {
    row.appendChild(el('span', 'resvalue', setter.value));
    return row;
  }

  const input = document.createElement('input');
  input.type = 'text';
  input.className = 'setterval';
  input.value = setter.value;
  input.title = 'Press Enter to apply — the style re-applies to its live consumers and the setter is written back';
  input.onkeydown = async (e) => {
    if (e.key !== 'Enter') return;
    const r = await (await fetch('/api/resources/set-setter', {
      method: 'POST',
      body: JSON.stringify({ key: styleEntry.key, property: setter.property, value: input.value }),
    })).json();
    input.classList.toggle('bad', !r.ok);
    markRecorded(input, r);
    if (r.ok) cookbookOnEdit();
  };
  row.appendChild(input);
  return row;
}

function filterResources(query) {
  query = (query || '').trim().toLowerCase();
  for (const group of document.querySelectorAll('.resgroup')) {
    let any = false;
    for (const row of group.querySelectorAll('.resrow')) {
      const hit = !query || (row.dataset.search || '').includes(query);
      row.classList.toggle('hiddenbyfilter', !hit);
      if (hit) any = true;
    }
    group.classList.toggle('hiddenbyfilter', !any);
  }
}

// Applied live but not recorded for the XAML Updater — make that state impossible to miss.
function markRecorded(input, r) {
  const liveOnly = r.ok && r.recorded === false;
  input.classList.toggle('warn', liveOnly);
  input.title = liveOnly
    ? 'Applied live only — not recorded for XAML. Enable the ✎ XAML toggle (and make sure the dictionary comes from a source file).'
    : 'Press Enter to apply — DynamicResource consumers update live and the value is written back to the dictionary file';
}

// The resource a setter's "{StaticResource X}" points at, editable right underneath it.
function renderSetterResourceRow(setter) {
  const row = el('div', 'resrow setter subres');
  row.dataset.search = (setter.resourceKey + ' ' + setter.resourceValue).toLowerCase();
  row.appendChild(el('span', 'reskey', '\u21b3 ' + setter.resourceKey));

  if (setter.resourceKind === 'color' || setter.resourceKind === 'brush') {
    const swatch = el('span', 'swatch');
    swatch.style.background = setter.resourceValue;
    row.appendChild(swatch);
  }
  const input = document.createElement('input');
  input.type = 'text';
  input.className = 'setterval';
  input.value = setter.resourceValue;
  input.title = 'Edits the resource itself — every consumer of ' + setter.resourceKey + ' is affected';
  input.onkeydown = async (e) => {
    if (e.key !== 'Enter') return;
    const r = await (await fetch('/api/resources/set', {
      method: 'POST', body: JSON.stringify({ key: setter.resourceKey, value: input.value }),
    })).json();
    input.classList.toggle('bad', !r.ok);
    markRecorded(input, r);
    if (r.ok) cookbookOnEdit();
  };
  row.appendChild(input);
  return row;
}
