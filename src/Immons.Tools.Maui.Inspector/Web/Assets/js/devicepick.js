// Header device picker: point this whole panel (tree, properties, mirror, resources…)
// at another running app — e.g. inspect the phone layout from the tablet's portal, or a physical
// phone on the Wi-Fi from the simulator's portal on the Mac.

let devicePickScanning = false;
let lanScanRunning = false;
const LAN_PORTS = [9295, 9296, 9297, 9298, 9299];
const PICK_SCAN_LAN = '__scan_lan__';
const PICK_ADD = '__add__';

// Hosts beyond this page's own: found by "Scan local network…" or typed in "Add address…"
// ("192.168.1.23:9295"), plus the hosts of saved mirror targets.
function savedInspectorHosts() {
  let saved = [];
  try { saved = JSON.parse(localStorage.getItem('hvHosts') || '[]'); } catch { /* none */ }
  try {
    for (const target of JSON.parse(localStorage.getItem('hvMirrors') || '[]')) {
      const m = target.url.match(/^https?:\/\/([^/]+)$/);
      if (m) saved.push(m[1]);
    }
  } catch { /* no saved targets */ }
  return [...new Set(saved)];
}

function rememberInspectorHost(hostPort) {
  const saved = new Set(savedInspectorHosts());
  saved.add(hostPort);
  try { localStorage.setItem('hvHosts', JSON.stringify([...saved])); } catch { /* storage full/blocked */ }
}

// One /api/ping with a short timeout; null when nothing (or not an inspector) answers.
async function pingInspector(base, timeoutMs) {
  try {
    const ctl = new AbortController();
    const timer = setTimeout(() => ctl.abort(), timeoutMs);
    const r = await window.fetch(base + '/api/ping', { signal: ctl.signal });
    clearTimeout(timer);
    const d = await r.json();
    return d && d.instance ? d : null;
  } catch {
    return null;
  }
}

function inspectorLabel(d) {
  return d.label || (d.app + ' · ' + d.device);
}

// Every URL the picker probes on a refresh: this page's host on the default range and on the
// ports of saved targets, plus each remembered host:port as is.
function devicePickCandidates() {
  const urls = new Set();
  const ports = new Set();
  for (let port = 9295; port <= 9309; port++) ports.add(port);
  const saved = savedInspectorHosts();
  for (const hostPort of saved) {
    const m = hostPort.match(/:(\d+)$/);
    if (m) ports.add(parseInt(m[1], 10));
    urls.add('http://' + hostPort);
  }
  for (const port of ports) urls.add('http://' + location.hostname + ':' + port);
  urls.delete(location.origin);
  return [...urls];
}

async function refreshDevicePick() {
  if (devicePickScanning) return;
  devicePickScanning = true;
  try {
    const own = await pingInspector(location.origin, 1500);
    const known = new Map();
    await Promise.all(devicePickCandidates().map(async (base) => {
      const d = await pingInspector(base, 700);
      // The same app is often reachable twice (simulator: localhost and the Mac's LAN address).
      if (d && (!own || d.instance !== own.instance) && !known.has(d.instance))
        known.set(d.instance, { url: base, label: inspectorLabel(d) });
    }));
    renderDevicePick(known);
  } finally {
    devicePickScanning = false;
  }
}

function renderDevicePick(known, scanStatus) {
  const pick = document.getElementById('devicepick');
  const current = pick.value && !pick.value.startsWith('__') ? pick.value : (window.apiBase || '');
  pick.innerHTML = '';
  pick.appendChild(new Option('This device', ''));
  for (const device of known.values()) {
    const where = device.url.replace(/^https?:\/\//, '');
    const short = where.startsWith(location.hostname + ':') ? where.split(':').pop() : where;
    pick.appendChild(new Option(device.label + ' (' + short + ')', device.url));
  }
  // Keep a currently-picked device selectable even if the rescan missed it this round.
  if (current && ![...pick.options].some(o => o.value === current))
    pick.appendChild(new Option(current, current));
  pick.appendChild(new Option(scanStatus || '🔍 Scan local network…', PICK_SCAN_LAN));
  pick.appendChild(new Option('＋ Add address…', PICK_ADD));
  pick.value = current;
}

async function onDevicePick(pick) {
  if (pick.value === PICK_SCAN_LAN) {
    pick.value = window.apiBase || '';
    await scanLocalNetwork();
    return;
  }
  if (pick.value === PICK_ADD) {
    pick.value = window.apiBase || '';
    const typed = (prompt('Device address — IP, IP:port or URL (e.g. 192.168.1.23:9295):') || '').trim();
    if (!typed) return;
    const hostPort = typed.replace(/^https?:\/\//, '').replace(/\/.*$/, '');
    const candidates = /:\d+$/.test(hostPort) ? [hostPort] : LAN_PORTS.map(p => hostPort + ':' + p);
    for (const candidate of candidates) {
      if (await pingInspector('http://' + candidate, 1500)) {
        rememberInspectorHost(candidate);
        await refreshDevicePick();
        document.getElementById('devicepick').value = 'http://' + candidate;
        await switchDevice('http://' + candidate);
        return;
      }
    }
    alert('No inspector answered at ' + typed + ' — is the app in the foreground and on the same Wi-Fi?');
    return;
  }
  await switchDevice(pick.value);
}

// Asks this page's own app to sweep the /24 of its Wi-Fi on ports 9295-9299 (GET /api/discover —
// for an iOS simulator app that is the Mac's network). The browser cannot sweep by itself: ~1 300
// fetches to silent addresses exhaust its socket pool and stall every other request for many
// seconds. Hosts that answer are remembered, so later refreshes probe them directly.
async function scanLocalNetwork() {
  if (lanScanRunning) return;
  lanScanRunning = true;
  try {
    renderDevicePickStatus('🔍 Scanning the local network…');
    let result = null;
    try {
      const ctl = new AbortController();
      const timer = setTimeout(() => ctl.abort(), 20000);
      result = await (await window.fetch(location.origin + '/api/discover', { signal: ctl.signal })).json();
      clearTimeout(timer);
    } catch { /* older app without /api/discover, or it went away */ }
    if (!result) {
      renderDevicePickStatus('🔍 Scan local network… (not available — use Add address)');
      return;
    }
    for (const device of result.found || [])
      rememberInspectorHost(device.url.replace(/^https?:\/\//, ''));
    await refreshDevicePick();
    renderDevicePickStatus(result.hosts
      ? '🔍 Scan local network… (' + (result.found || []).length + ' found, ' + (result.ms / 1000).toFixed(1) + ' s)'
      : '🔍 Scan local network… (no Wi-Fi address — use Add address)');
  } finally {
    lanScanRunning = false;
  }
}

function renderDevicePickStatus(text) {
  const option = [...document.getElementById('devicepick').options].find(o => o.value === PICK_SCAN_LAN);
  if (option) option.textContent = text;
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
