// Header device picker: point this whole panel (tree, properties, mirror, resources…)
// at another running app — e.g. inspect the phone layout from the tablet's portal.

let devicePickScanning = false;

async function refreshDevicePick() {
  if (devicePickScanning) return;
  devicePickScanning = true;
  try {
    const pick = document.getElementById('devicepick');
    const known = new Map();

    // Same pool the Devices tab uses: default range + ports of saved mirror targets.
    const ports = new Set();
    for (let port = 9295; port <= 9309; port++) ports.add(port);
    try {
      for (const target of JSON.parse(localStorage.getItem('hvMirrors') || '[]')) {
        const m = target.url.match(/:(\d+)$/);
        if (m) ports.add(parseInt(m[1], 10));
      }
    } catch { /* no saved targets */ }

    await Promise.all([...ports].map(async (port) => {
      const base = 'http://' + location.hostname + ':' + port;
      if (base === location.origin) return;
      try {
        const ctl = new AbortController();
        setTimeout(() => ctl.abort(), 700);
        const d = await (await window.fetch(base + '/api/ping', { signal: ctl.signal })).json();
        known.set(d.instance, { url: base, label: d.app + ' · ' + d.device });
      } catch { /* nothing on this port */ }
    }));

    const current = pick.value;
    pick.innerHTML = '';
    pick.appendChild(new Option('This device', ''));
    for (const device of known.values())
      pick.appendChild(new Option(device.label + ' (' + device.url.split(':').pop() + ')', device.url));
    // Keep a currently-picked device selectable even if the rescan missed it this round.
    if (current && ![...pick.options].some(o => o.value === current))
      pick.appendChild(new Option(current, current));
    pick.value = current;
  } finally {
    devicePickScanning = false;
  }
}

async function switchDevice(base) {
  window.apiBase = base;

  // Everything selected belonged to the previous app — start clean on the new one.
  selectedId = null;
  compareId = null;
  if (typeof closeStructureMenu === 'function') closeStructureMenu();
  document.getElementById('props').innerHTML =
    '<div id="empty">Click an element in the tree.</div>';

  await refreshAll();
  showVersion();
  if (!document.getElementById('resback').hidden) loadResources();
  cookbookReset();
}

refreshDevicePick();   // populate the dropdown right away — the scan is quick and async
