// Remote inspection: when another device is picked in the header, every relative
// /api call is redirected to that app (CORS is open on the inspector server).
window.apiBase = '';
const nativeFetch = window.fetch.bind(window);
window.fetch = (url, opts) =>
  (typeof url === 'string' && url.startsWith('/') && window.apiBase)
    ? nativeFetch(window.apiBase + url, opts)
    : nativeFetch(url, opts);

// Shared client state, read/written by the other modules.
let selectedId = null;
let compareId = null;
let measure = false;
let wysiwyg = false;
let selectMode = false;
let overlayShown = false;
let debugPaint = false;
let perfOn = false;
let slowOn = false;
let syncConnected = false;
let histSeq = 0;
let mirrorTimer = null;
let windowDp = null; // [w, h] parsed from the tree payload
let currentSource = null; // XAML source identity of the selected element (multi-device key)
let currentIdentity = { elementName: '', automationId: '', type: '' };
let allInstances = false; // apply edits to every element sharing this XAML source
let activeView = 'inspector';

// Edits of these properties change tree labels — rebuild the tree afterwards.
const TREE_LABEL_PROPS = ['AutomationId', 'Text', 'Title', 'Source'];

function updateHint() {
  const hint = document.getElementById('hint');
  if (measure)
    hint.textContent = 'measure: pick the second element here or on the device';
  else if (wysiwyg)
    hint.textContent = syncConnected
      ? 'Sync tool ✓'
      : 'Sync tool not running — in your app source folder run: maui-inspector-sync';
  else
    hint.textContent = '';
}

// #AARRGGBB → rgba(); #RRGGBB stays as-is.
function cssColor(hex) {
  if (hex && hex.length === 9) {
    const a = parseInt(hex.slice(1, 3), 16) / 255;
    return 'rgba(' + parseInt(hex.slice(3, 5), 16) + ',' + parseInt(hex.slice(5, 7), 16) + ',' +
           parseInt(hex.slice(7, 9), 16) + ',' + a.toFixed(3) + ')';
  }
  return hex;
}
let currentTemplated = false;
