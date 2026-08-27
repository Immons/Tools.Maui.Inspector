// Top-level views: Inspector (tree + properties), Cookbook (style gallery), full-screen
// Network (requests + mocks), Logs, Memory (leak snapshots) and Devices (multi-device targets).
// Inspector-only tools hide elsewhere.
const VIEWS = ['inspector', 'cookbook', 'network', 'logs', 'memory', 'devices'];

function showView(name) {
  if (activeView === 'memory' && name !== 'memory') stopMemory();
  activeView = name;
  // Views are linkable: /#network, /#logs, /#memory, /#devices (also used for docs screenshots).
  if (location.hash !== '#' + name)
    history.replaceState(null, '', '#' + name);
  for (const v of VIEWS) {
    document.getElementById('view-' + v).hidden = v !== name;
    document.getElementById('nav-' + v).classList.toggle('active', v === name);
  }
  document.getElementById('globalbar').hidden = name !== 'inspector';
  if (name === 'network') refreshNetworkView();
  else if (name === 'cookbook') showCookbook();
  else if (name === 'logs') loadLogs();
  else if (name === 'memory') showMemory();
  else if (name === 'devices') renderMirrors();
}

// Network sub-views: live traffic (requests/breakpoints) vs mock rules & scenarios.
let networkSub = 'requests';

function showNetworkSub(name) {
  networkSub = name;
  document.getElementById('sub-requests').classList.toggle('active', name === 'requests');
  document.getElementById('sub-mocks').classList.toggle('active', name === 'mocks');
  document.getElementById('subview-requests').hidden = name !== 'requests';
  document.getElementById('subview-mocks').hidden = name !== 'mocks';
  refreshNetworkView();
}

function refreshNetworkView() {
  if (networkSub === 'requests') loadNetwork();
  else loadMocks();
}

// Open the view named in the URL hash on load.
document.addEventListener('DOMContentLoaded', () => {
  const name = location.hash.replace('#', '');
  if (VIEWS.includes(name) && name !== 'inspector')
    showView(name);
  if (name === 'mocks') { showView('network'); showNetworkSub('mocks'); }
});
