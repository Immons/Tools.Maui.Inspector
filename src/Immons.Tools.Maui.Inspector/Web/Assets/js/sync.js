// Device ↔ web sync: poll selection, compare target and mode flags every second.

// Asked once per page load: with XAML editing off, edits stay runtime-only. When the
// updater already polls, offer to enable; when it doesn't, show how to start it.
let xamlEditPromptShown = false;

function maybeOfferXamlEditing(d) {
  if (d.wysiwyg || d.sync === undefined || xamlEditPromptShown) return;
  // Wait for the first tree payload — the instructions need the device platform and port.
  if (window.deviceStr === undefined) return;
  xamlEditPromptShown = true;
  openSyncPrompt(d.sync);
}

function enableXamlEditing() {
  setWysiwygUi(true);
  fetch('/api/wysiwyg', { method: 'POST', body: JSON.stringify({ on: true }) });
}

function openSyncPrompt(connected) {
  const body = document.getElementById('syncbody');
  const ok = document.getElementById('syncok');
  body.innerHTML = '';

  if (connected) {
    document.getElementById('synctitle').textContent = 'XAML Updater is connected';
    body.textContent = 'Enable XAML editing so property and structure edits are written back to your source files?';
    ok.textContent = 'Enable';
    ok.disabled = false;
    ok.onclick = () => { enableXamlEditing(); closeSyncPrompt(); };
  } else {
    document.getElementById('synctitle').textContent = 'XAML Updater is not running';
    const hostPort = location.port || '80';
    const devicePort = window.devicePort || hostPort;
    const isAndroid = (window.deviceStr || '').includes('Android');

    const intro = document.createElement('div');
    intro.textContent = 'To write edits back to your XAML sources, start the updater in your project folder:';
    body.appendChild(intro);
    if (isAndroid && String(devicePort) !== String(hostPort)) {
      const fwd = document.createElement('div');
      fwd.textContent = 'Android device — forward the port first:';
      body.appendChild(fwd);
      body.appendChild(Object.assign(document.createElement('code'),
        { textContent: 'adb forward tcp:' + hostPort + ' tcp:' + devicePort }));
    }
    // The updater scans 9295-9309 and watches every inspector it finds by itself —
    // --app is only needed for ports outside that range (custom forwards).
    const inScanRange = +hostPort >= 9295 && +hostPort <= 9309;
    body.appendChild(Object.assign(document.createElement('code'),
      { textContent: inScanRange ? 'maui-inspector-sync' : 'maui-inspector-sync --app http://localhost:' + hostPort }));
    const install = document.createElement('div');
    install.textContent = 'No command? Install it once: dotnet tool install -g Immons.Tools.Maui.Inspector.Sync';
    body.appendChild(install);

    ok.textContent = 'I started it — check';
    ok.disabled = false;
    ok.onclick = () => verifySyncStarted(body, ok);
  }
  document.getElementById('syncback').hidden = false;
}

// "I started it" is only believed once the updater actually polls the app.
async function verifySyncStarted(body, ok) {
  ok.disabled = true;
  ok.textContent = 'Checking…';
  document.querySelector('#syncbody .syncwarn')?.remove();
  for (let attempt = 0; attempt < 6; attempt++) {
    try {
      const d = await (await fetch('/api/selection')).json();
      if (d.sync) {
        enableXamlEditing();
        closeSyncPrompt();
        return;
      }
    } catch { /* app briefly unreachable — keep trying */ }
    await new Promise(r => setTimeout(r, 1000));
  }
  ok.disabled = false;
  ok.textContent = 'I started it — check';
  const warn = document.createElement('div');
  warn.className = 'syncwarn';
  warn.textContent = 'Updater still not detected — is the command running and pointed at this URL?';
  body.appendChild(warn);
}

function closeSyncPrompt() {
  document.getElementById('syncback').hidden = true;
}

setInterval(async () => {
  try {
    // A backgrounded app does not refuse the connection — iOS suspends the whole process, so the
    // request simply never comes back. Without this timeout the poll hangs forever and the panel
    // keeps showing the last (green) state while nothing works.
    const ctl = new AbortController();
    const timer = setTimeout(() => ctl.abort(), 2500);
    let r;
    try {
      r = await fetch('/api/selection', { signal: ctl.signal });
    } finally {
      clearTimeout(timer);
    }
    const d = await r.json();
    setConnected(true, d.fg !== false);

    // Mirror adorners (alignment pins, grid designer) follow the device-side selection.
    window.selMeta = { id: d.id ?? null, rect: d.rect || null, h: d.halign || null, v: d.valign || null };
    if (typeof renderMirrorAdorners === 'function') renderMirrorAdorners();

    if (d.measure !== measure)
      setMeasureUi(d.measure);

    if (d.wysiwyg !== wysiwyg)
      setWysiwygUi(d.wysiwyg);

    maybeOfferXamlEditing(d);

    if (d.select !== undefined && d.select !== selectMode)
      setSelectUi(d.select);

    if (d.overlay !== undefined && d.overlay !== overlayShown)
      setOverlayUi(d.overlay);

    if (d.paint !== undefined && d.paint !== debugPaint)
      setPaintUi(d.paint);

    if (d.slow !== undefined && d.slow !== slowOn)
      setSlowUi(d.slow);

    if (d.perf !== undefined) {
      if ((d.perf != null) !== perfOn) setPerfUi(d.perf != null);
      document.getElementById('perfout').textContent =
        d.perf ? (d.perf.fps + ' fps · avg ' + d.perf.avg + ' ms · worst ' + d.perf.worst + ' ms') : '';
    }

    if (d.sync !== undefined && d.sync !== syncConnected) {
      syncConnected = d.sync;
      updateHint();
    }

    if (d.hseq !== undefined && d.hseq !== histSeq) {
      const first = histSeq === 0;
      histSeq = d.hseq;
      if (!document.getElementById('histpanel').hidden)
        await loadHistory();
      // Edits made anywhere (on the device too) change what the cookbook tiles show.
      if (!first) cookbookOnEdit();
    }

    if (activeView === 'network') refreshNetworkView();
    else if (activeView === 'logs') loadLogs();

    const cmp = d.compare ?? null;
    if (cmp !== compareId) {
      compareId = cmp;
      if (compareId != null && !document.querySelector('.row[data-id="' + compareId + '"]'))
        await refreshAll();
      if (compareId != null) reveal(compareId);
      markRows();
    }

    if (d.id != null && d.id !== selectedId) {
      selectedId = d.id;
      if (!reveal(d.id)) {
        await refreshAll();
        reveal(d.id);
      }
      markRows();
      await loadProps(d.id, false);
    }
  } catch (e) {
    // Timed out = the process is alive but parked (backgrounded). Refused = it is really gone.
    if (e && e.name === 'AbortError')
      setConnected(true, false);
    else
      setConnected(false);
  }
}, 1000);

refreshAll();

showVersion();   // header: package version + "newer available" hint
